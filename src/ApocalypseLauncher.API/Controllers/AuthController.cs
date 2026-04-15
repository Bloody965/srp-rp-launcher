using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApocalypseLauncher.API.Data;
using ApocalypseLauncher.API.Models;
using ApocalypseLauncher.API.Services;
using System.Security.Claims;

namespace ApocalypseLauncher.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly JwtService _jwtService;
    private readonly PasswordService _passwordService;
    private readonly RateLimitService _rateLimitService;
    private readonly EmailService _emailService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppDbContext context,
        JwtService jwtService,
        PasswordService passwordService,
        RateLimitService rateLimitService,
        EmailService emailService,
        ILogger<AuthController> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _passwordService = passwordService;
        _rateLimitService = rateLimitService;
        _emailService = emailService;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Защита от SQL инъекций
        if (ValidationService.ContainsSqlInjection(request.Username) ||
            ValidationService.ContainsSqlInjection(request.Email) ||
            ValidationService.ContainsSqlInjection(request.Password))
        {
            await LogAction(null, "REGISTER_SQL_INJECTION_ATTEMPT", $"Username: {request.Username}, Email: {request.Email}", ip);
            return BadRequest(new AuthResponse { Success = false, Message = "Обнаружена попытка SQL инъекции" });
        }

        // Rate limiting - 3 регистрации в час с одного IP
        if (_rateLimitService.IsRateLimited($"register:{ip}", 3, TimeSpan.FromHours(1)))
        {
            await LogAction(null, "REGISTER_RATE_LIMITED", $"IP: {ip}", ip);
            return BadRequest(new AuthResponse
            {
                Success = false,
                Message = "Слишком много попыток регистрации. Попробуйте позже."
            });
        }

        // Валидация
        if (!ValidationService.IsValidUsername(request.Username))
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Имя пользователя должно быть от 3 до 16 символов (только буквы, цифры и _)" });
        }

        if (!ValidationService.IsValidEmail(request.Email))
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Некорректный email адрес" });
        }

        var (isValid, error) = _passwordService.ValidatePasswordStrength(request.Password);
        if (!isValid)
        {
            return BadRequest(new AuthResponse { Success = false, Message = error });
        }

        // Проверка существования пользователя
        if (await _context.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower()))
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Пользователь с таким именем уже существует" });
        }

        if (await _context.Users.AnyAsync(u => u.Email.ToLower() == request.Email.ToLower()))
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Email уже зарегистрирован" });
        }

        // Создание пользователя
        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = _passwordService.HashPassword(request.Password),
            MinecraftUUID = _passwordService.GenerateMinecraftUUID(request.Username),
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            IsWhitelisted = false // По умолчанию не в whitelist
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await LogAction(user.Id, "REGISTER", $"Username: {user.Username}, Email: {user.Email}", ip);

        // Генерация токена
        var token = _jwtService.GenerateToken(user.Id, user.Username, user.Email);

        // Создание сессии
        var session = new LoginSession
        {
            UserId = user.Id,
            Token = token,
            IpAddress = ip,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        _context.LoginSessions.Add(session);
        await _context.SaveChangesAsync();

        _logger.LogInformation($"New user registered: {user.Username} (ID: {user.Id})");

        return Ok(new AuthResponse
        {
            Success = true,
            Token = token,
            Message = "Регистрация успешна",
            User = new UserInfo
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                MinecraftUUID = user.MinecraftUUID,
                IsWhitelisted = user.IsWhitelisted
            }
        });
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Rate limiting - 5 попыток входа в 15 минут
        if (_rateLimitService.IsRateLimited($"login:{ip}", 5, TimeSpan.FromMinutes(15)))
        {
            await LogAction(null, "LOGIN_RATE_LIMITED", $"IP: {ip}", ip);
            return BadRequest(new AuthResponse
            {
                Success = false,
                Message = "Слишком много попыток входа. Попробуйте через 15 минут."
            });
        }

        // Поиск пользователя
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());

        if (user == null || !_passwordService.VerifyPassword(request.Password, user.PasswordHash))
        {
            await LogAction(null, "LOGIN_FAILED", $"Username: {request.Username}, IP: {ip}", ip);
            return Unauthorized(new AuthResponse
            {
                Success = false,
                Message = "Неверное имя пользователя или пароль"
            });
        }

        // Проверка бана
        if (user.IsBanned)
        {
            await LogAction(user.Id, "LOGIN_BANNED", $"Reason: {user.BanReason}", ip);
            return Unauthorized(new AuthResponse
            {
                Success = false,
                Message = $"Аккаунт заблокирован. Причина: {user.BanReason}"
            });
        }

        // Проверка активности
        if (!user.IsActive)
        {
            return Unauthorized(new AuthResponse
            {
                Success = false,
                Message = "Аккаунт деактивирован"
            });
        }

        // Сброс rate limit при успешном входе
        _rateLimitService.ResetLimit($"login:{ip}");

        // Обновление последнего входа
        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Генерация токена
        var token = _jwtService.GenerateToken(user.Id, user.Username, user.Email);

        // Создание сессии
        var session = new LoginSession
        {
            UserId = user.Id,
            Token = token,
            IpAddress = ip,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        _context.LoginSessions.Add(session);
        await _context.SaveChangesAsync();

        await LogAction(user.Id, "LOGIN_SUCCESS", $"IP: {ip}", ip);

        _logger.LogInformation($"User logged in: {user.Username} (ID: {user.Id})");

        return Ok(new AuthResponse
        {
            Success = true,
            Token = token,
            Message = "Вход выполнен успешно",
            User = new UserInfo
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                MinecraftUUID = user.MinecraftUUID,
                IsWhitelisted = user.IsWhitelisted
            }
        });
    }

    [HttpPost("verify")]
    public async Task<ActionResult<AuthResponse>> VerifyToken()
    {
        var token = Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

        if (string.IsNullOrEmpty(token))
        {
            return Unauthorized(new AuthResponse { Success = false, Message = "Токен не предоставлен" });
        }

        var principal = _jwtService.ValidateToken(token);
        if (principal == null)
        {
            return Unauthorized(new AuthResponse { Success = false, Message = "Недействительный токен" });
        }

        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new AuthResponse { Success = false, Message = "Недействительный токен" });
        }

        // Проверка сессии
        var session = await _context.LoginSessions
            .FirstOrDefaultAsync(s => s.Token == token && s.UserId == userId && !s.IsRevoked);

        if (session == null || session.ExpiresAt < DateTime.UtcNow)
        {
            return Unauthorized(new AuthResponse { Success = false, Message = "Сессия истекла" });
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null || !user.IsActive || user.IsBanned)
        {
            return Unauthorized(new AuthResponse { Success = false, Message = "Аккаунт недоступен" });
        }

        return Ok(new AuthResponse
        {
            Success = true,
            Message = "Токен действителен",
            User = new UserInfo
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                MinecraftUUID = user.MinecraftUUID,
                IsWhitelisted = user.IsWhitelisted
            }
        });
    }

    [HttpPost("request-reset-code")]
    public async Task<ActionResult<AuthResponse>> RequestResetCode([FromBody] RequestResetCodeRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Защита от SQL инъекций
        if (ValidationService.ContainsSqlInjection(request.Email))
        {
            await LogAction(null, "RESET_CODE_SQL_INJECTION", $"Email: {request.Email}", ip);
            return BadRequest(new AuthResponse { Success = false, Message = "Обнаружена попытка SQL инъекции" });
        }

        // Rate limiting - 3 попытки в час
        if (_rateLimitService.IsRateLimited($"reset:{ip}", 3, TimeSpan.FromHours(1)))
        {
            await LogAction(null, "RESET_CODE_RATE_LIMITED", $"IP: {ip}", ip);
            return BadRequest(new AuthResponse
            {
                Success = false,
                Message = "Слишком много попыток. Попробуйте через час."
            });
        }

        // Валидация email
        if (!ValidationService.IsValidEmail(request.Email))
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Неверный формат email" });
        }

        // Поиск пользователя по email
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());

        if (user == null)
        {
            // Не раскрываем существование пользователя
            await LogAction(null, "RESET_CODE_USER_NOT_FOUND", $"Email: {request.Email}, IP: {ip}", ip);
            return Ok(new AuthResponse
            {
                Success = true,
                Message = "Если email существует, код был отправлен на почту"
            });
        }

        // Генерация 6-значного кода
        var random = new Random();
        var code = random.Next(100000, 999999).ToString();

        // Сохранение кода в базу
        var resetCode = new PasswordResetCode
        {
            UserId = user.Id,
            Code = code,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            IsUsed = false
        };

        _context.PasswordResetCodes.Add(resetCode);
        await _context.SaveChangesAsync();

        // Отправка email
        var emailSent = await _emailService.SendPasswordResetEmailAsync(user.Email, user.Username, code);

        if (!emailSent)
        {
            await LogAction(user.Id, "RESET_CODE_EMAIL_FAILED", $"IP: {ip}", ip);
            return StatusCode(500, new AuthResponse
            {
                Success = false,
                Message = "Ошибка отправки email. Попробуйте позже."
            });
        }

        await LogAction(user.Id, "RESET_CODE_SENT", $"IP: {ip}", ip);

        return Ok(new AuthResponse
        {
            Success = true,
            Message = "Код для сброса пароля отправлен на вашу почту"
        });
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult<AuthResponse>> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Защита от SQL инъекций
        if (ValidationService.ContainsSqlInjection(request.Email) ||
            ValidationService.ContainsSqlInjection(request.NewPassword) ||
            (request.Code != null && ValidationService.ContainsSqlInjection(request.Code)))
        {
            await LogAction(null, "RESET_PASSWORD_SQL_INJECTION", $"Email: {request.Email}", ip);
            return BadRequest(new AuthResponse { Success = false, Message = "Обнаружена попытка SQL инъекции" });
        }

        // Rate limiting - 5 попыток в 15 минут
        if (_rateLimitService.IsRateLimited($"reset_verify:{ip}", 5, TimeSpan.FromMinutes(15)))
        {
            await LogAction(null, "RESET_PASSWORD_RATE_LIMITED", $"IP: {ip}", ip);
            return BadRequest(new AuthResponse
            {
                Success = false,
                Message = "Слишком много попыток. Попробуйте через 15 минут."
            });
        }

        // Валидация email
        if (!ValidationService.IsValidEmail(request.Email))
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Неверный формат email" });
        }

        // Валидация кода
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length != 6)
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Введите 6-значный код из письма" });
        }

        // Валидация нового пароля
        var (isValid, error) = _passwordService.ValidatePasswordStrength(request.NewPassword);
        if (!isValid)
        {
            return BadRequest(new AuthResponse { Success = false, Message = error });
        }

        // Поиск пользователя по email
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());

        if (user == null)
        {
            await LogAction(null, "RESET_PASSWORD_USER_NOT_FOUND", $"Email: {request.Email}, IP: {ip}", ip);
            return BadRequest(new AuthResponse
            {
                Success = false,
                Message = "Неверный email или код"
            });
        }

        // Проверка кода
        var resetCode = await _context.PasswordResetCodes
            .Where(r => r.UserId == user.Id && r.Code == request.Code && !r.IsUsed && r.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (resetCode == null)
        {
            await LogAction(user.Id, "RESET_PASSWORD_INVALID_CODE", $"IP: {ip}", ip);
            return BadRequest(new AuthResponse
            {
                Success = false,
                Message = "Неверный или истекший код"
            });
        }

        // Обновление пароля
        user.PasswordHash = _passwordService.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        // Отметка кода как использованного
        resetCode.IsUsed = true;
        resetCode.UsedAt = DateTime.UtcNow;

        // Отзыв всех активных сессий
        var sessions = await _context.LoginSessions
            .Where(s => s.UserId == user.Id && !s.IsRevoked)
            .ToListAsync();

        foreach (var session in sessions)
        {
            session.IsRevoked = true;
        }

        await _context.SaveChangesAsync();

        await LogAction(user.Id, "RESET_PASSWORD_SUCCESS", $"IP: {ip}", ip);

        return Ok(new AuthResponse
        {
            Success = true,
            Message = "Пароль успешно изменен. Войдите с новым паролем."
        });
    }

    private async Task LogAction(int? userId, string action, string? details, string ip)
    {
        var log = new AuditLog
        {
            UserId = userId,
            Action = action,
            Details = details,
            IpAddress = ip,
            CreatedAt = DateTime.UtcNow
        };

        _context.AuditLogs.Add(log);
        await _context.SaveChangesAsync();
    }
}
