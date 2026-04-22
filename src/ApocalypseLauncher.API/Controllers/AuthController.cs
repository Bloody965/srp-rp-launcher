using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApocalypseLauncher.API.Data;
using ApocalypseLauncher.API.Models;
using ApocalypseLauncher.API.Services;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace ApocalypseLauncher.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly JwtService _jwtService;
    private readonly PasswordService _passwordService;
    private readonly RateLimitService _rateLimitService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly UserIdentityConsistencyService _identityConsistency;
    private readonly WebHandoffService _webHandoffService;

    public AuthController(
        AppDbContext context,
        JwtService jwtService,
        PasswordService passwordService,
        RateLimitService rateLimitService,
        IConfiguration configuration,
        ILogger<AuthController> logger,
        UserIdentityConsistencyService identityConsistency,
        WebHandoffService webHandoffService)
    {
        _context = context;
        _jwtService = jwtService;
        _passwordService = passwordService;
        _rateLimitService = rateLimitService;
        _configuration = configuration;
        _logger = logger;
        _identityConsistency = identityConsistency;
        _webHandoffService = webHandoffService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Защита от SQL инъекций
        if (ValidationService.ContainsSqlInjection(request.Username) ||
            ValidationService.ContainsSqlInjection(request.Password))
        {
            await LogAction(null, "REGISTER_SQL_INJECTION_ATTEMPT", $"Username: {request.Username}", ip);
            return BadRequest(new AuthResponse { Success = false, Message = "Обнаружена попытка SQL инъекции" });
        }

        // Rate limiting - 3 регистрации в час с одного IP
        if (_rateLimitService.IsRateLimited($"register:{ip}", 3, TimeSpan.FromHours(1)))
        {
            await LogAction(null, "REGISTER_RATE_LIMITED", $"IP: {ip}", ip);
            return BadRequest(new AuthResponse
            {
                Success = false,
                Message = "Слишком много попыток регистрации. Попробуйте через 1 час."
            });
        }

        // Валидация
        if (!ValidationService.IsValidUsername(request.Username))
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Имя пользователя должно быть от 3 до 16 символов (только буквы, цифры и _)" });
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

        // Генерация кода восстановления (16 символов)
        var recoveryCode = _passwordService.GenerateRecoveryCode();

        // Создание пользователя
        var user = new User
        {
            Username = request.Username,
            Email = null, // Не храним email
            PasswordHash = _passwordService.HashPassword(request.Password),
            RecoveryCode = _passwordService.HashRecoveryCode(recoveryCode),
            MinecraftUUID = _passwordService.GenerateMinecraftUUID(request.Username),
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            IsWhitelisted = false
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await LogAction(user.Id, "REGISTER", $"Username: {user.Username}", ip);

        // Генерация токена
        var token = _jwtService.GenerateToken(user.Id, user.Username, user.Email ?? "");

        // Создание сессии
        var session = new LoginSession
        {
            UserId = user.Id,
            Token = _jwtService.HashToken(token),
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
            Message = "Регистрация успешна! СОХРАНИТЕ КОД ВОССТАНОВЛЕНИЯ - он понадобится для сброса пароля!",
            RecoveryCode = recoveryCode, // Показываем код только один раз
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

        _identityConsistency.RepairMinecraftUuidIfMismatch(user);

        if (await HasPendingAdminPasswordResetAsync(user.Id))
        {
            await LogAction(user.Id, "LOGIN_BLOCKED_FORCE_PASSWORD_RESET", $"IP: {ip}", ip);
            return Unauthorized(new AuthResponse
            {
                Success = false,
                Message = "Пароль был сброшен администратором. Установите новый пароль.",
                RequiresPasswordReset = true,
                NotificationMessage = "Ваш пароль был сброшен администратором. Для входа необходимо установить новый пароль."
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
            Token = _jwtService.HashToken(token),
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
        var tokenHash = _jwtService.HashToken(token);
        var session = await _context.LoginSessions
            .FirstOrDefaultAsync(s => s.Token == tokenHash && s.UserId == userId && !s.IsRevoked);

        if (session == null || session.ExpiresAt < DateTime.UtcNow)
        {
            return Unauthorized(new AuthResponse { Success = false, Message = "Сессия истекла" });
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null || !user.IsActive || user.IsBanned)
        {
            return Unauthorized(new AuthResponse { Success = false, Message = "Аккаунт недоступен" });
        }

        await _identityConsistency.EnsureMinecraftUuidPersistedAsync(user);

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

    /// <summary>Создать одноразовый код для входа в лаунчер (нужна активная сессия сайта с JWT).</summary>
    [HttpPost("web-handoff/create")]
    [Authorize]
    public async Task<ActionResult<WebHandoffCreateResponse>> CreateWebHandoff()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userIdClaim = GetCurrentUserIdFromClaims();
        if (userIdClaim == null)
        {
            return Unauthorized(new WebHandoffCreateResponse { Success = false, Message = "Нет авторизации" });
        }

        var uid = userIdClaim.Value;
        if (!await HasActiveSessionAsync(uid))
        {
            return Unauthorized(new WebHandoffCreateResponse { Success = false, Message = "Сессия недействительна или истекла" });
        }

        if (_rateLimitService.IsRateLimited($"handoff_create:{uid}", 10, TimeSpan.FromMinutes(1)))
        {
            return BadRequest(new WebHandoffCreateResponse
            {
                Success = false,
                Message = "Слишком часто создаются коды. Подождите минуту."
            });
        }

        if (!_webHandoffService.TryCreate(uid, out var code))
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new WebHandoffCreateResponse { Success = false, Message = "Не удалось создать код. Повторите позже." });
        }

        await LogAction(uid, "WEB_HANDOFF_CREATE", null, ip);
        return Ok(new WebHandoffCreateResponse
        {
            Success = true,
            HandoffCode = code,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2),
            Message = "Введите код в лаунчере в течение 2 минут. Код одноразовый."
        });
    }

    /// <summary>Обменять код с сайта на JWT (как при обычном входе).</summary>
    [HttpPost("web-handoff/redeem")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> RedeemWebHandoff([FromBody] WebHandoffRedeemRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (_rateLimitService.IsRateLimited($"handoff_redeem:{ip}", 40, TimeSpan.FromMinutes(1)))
        {
            return BadRequest(new AuthResponse
            {
                Success = false,
                Message = "Слишком много попыток. Подождите минуту."
            });
        }

        var code = request?.HandoffCode?.Trim() ?? string.Empty;
        if (code.Length < 12 || code.Length > 256)
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Некорректный код" });
        }

        if (!_webHandoffService.TryConsume(code, out var userId))
        {
            await LogAction(null, "WEB_HANDOFF_INVALID", $"IP: {ip}", ip);
            return BadRequest(new AuthResponse
            {
                Success = false,
                Message = "Код недействителен, истёк или уже использован"
            });
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null || !user.IsActive)
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Аккаунт недоступен" });
        }

        if (user.IsBanned)
        {
            await LogAction(user.Id, "WEB_HANDOFF_BANNED", $"IP: {ip}", ip);
            return Unauthorized(new AuthResponse
            {
                Success = false,
                Message = $"Аккаунт заблокирован. Причина: {user.BanReason}"
            });
        }

        _identityConsistency.RepairMinecraftUuidIfMismatch(user);

        if (await HasPendingAdminPasswordResetAsync(user.Id))
        {
            await LogAction(user.Id, "WEB_HANDOFF_BLOCKED_FORCE_PASSWORD_RESET", $"IP: {ip}", ip);
            return Unauthorized(new AuthResponse
            {
                Success = false,
                Message = "Пароль был сброшен администратором. Установите новый пароль.",
                RequiresPasswordReset = true,
                NotificationMessage = "Ваш пароль был сброшен администратором. Для входа необходимо установить новый пароль."
            });
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var token = _jwtService.GenerateToken(user.Id, user.Username, user.Email);

        var session = new LoginSession
        {
            UserId = user.Id,
            Token = _jwtService.HashToken(token),
            IpAddress = ip,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        _context.LoginSessions.Add(session);
        await _context.SaveChangesAsync();

        await LogAction(user.Id, "WEB_HANDOFF_REDEEM", $"IP: {ip}", ip);
        _logger.LogInformation("Web handoff redeem for user {Username} ({UserId})", user.Username, user.Id);

        return Ok(new AuthResponse
        {
            Success = true,
            Token = token,
            Message = "Вход через сайт выполнен",
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

    [HttpPost("reset-password")]
    public async Task<ActionResult<AuthResponse>> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Защита от SQL инъекций
        if (ValidationService.ContainsSqlInjection(request.Username) ||
            ValidationService.ContainsSqlInjection(request.RecoveryCode) ||
            ValidationService.ContainsSqlInjection(request.NewPassword))
        {
            await LogAction(null, "RESET_PASSWORD_SQL_INJECTION", $"Username: {request.Username}", ip);
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

        // Валидация username
        if (!ValidationService.IsValidUsername(request.Username))
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Неверное имя пользователя" });
        }

        // Валидация recovery code
        if (string.IsNullOrWhiteSpace(request.RecoveryCode) || request.RecoveryCode.Length < 16)
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Введите код восстановления (16 символов)" });
        }

        // Валидация нового пароля
        var (isValid, error) = _passwordService.ValidatePasswordStrength(request.NewPassword);
        if (!isValid)
        {
            return BadRequest(new AuthResponse { Success = false, Message = error });
        }

        // Поиск пользователя по username и recovery code
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());

        if (user == null || !_passwordService.VerifyRecoveryCode(request.RecoveryCode, user.RecoveryCode))
        {
            await LogAction(null, "RESET_PASSWORD_INVALID", $"Username: {request.Username}, IP: {ip}", ip);
            return BadRequest(new AuthResponse
            {
                Success = false,
                Message = "Неверное имя пользователя или код восстановления"
            });
        }

        // Обновление пароля
        user.PasswordHash = _passwordService.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        user.IsAdminPasswordResetRequired = false;
        user.AdminResetCodeHash = null;
        user.AdminResetCodeExpiresAt = null;

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

        _logger.LogInformation($"Password reset successful for user: {user.Username} (ID: {user.Id})");

        return Ok(new AuthResponse
        {
            Success = true,
            Message = "Пароль успешно изменен. Войдите с новым паролем."
        });
    }

    [HttpPost("reset-password-by-admin")]
    public async Task<ActionResult<AuthResponse>> ResetPasswordByAdmin([FromBody] ResetPasswordByAdminRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.ResetCode) ||
            string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Заполните все поля" });
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());
        if (user == null)
        {
            await LogAction(null, "FORCED_RESET_INVALID_CREDENTIALS", $"Username: {request.Username}, IP: {ip}", ip);
            return Unauthorized(new AuthResponse { Success = false, Message = "Неверное имя пользователя или код сброса" });
        }

        if (!await HasPendingAdminPasswordResetAsync(user.Id))
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Для аккаунта не требуется принудительная смена пароля" });
        }

        var providedCodeHash = HashText(request.ResetCode.Trim().ToUpperInvariant());
        if (string.IsNullOrWhiteSpace(user.AdminResetCodeHash) ||
            !string.Equals(user.AdminResetCodeHash, providedCodeHash, StringComparison.Ordinal) ||
            user.AdminResetCodeExpiresAt == null ||
            user.AdminResetCodeExpiresAt < DateTime.UtcNow)
        {
            await LogAction(user.Id, "FORCED_RESET_INVALID_CODE", $"IP: {ip}", ip);
            return Unauthorized(new AuthResponse { Success = false, Message = "Неверный или просроченный код сброса" });
        }

        var (isValid, error) = _passwordService.ValidatePasswordStrength(request.NewPassword);
        if (!isValid)
        {
            return BadRequest(new AuthResponse { Success = false, Message = error });
        }

        user.PasswordHash = _passwordService.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        user.IsAdminPasswordResetRequired = false;
        user.AdminResetCodeHash = null;
        user.AdminResetCodeExpiresAt = null;

        var sessions = await _context.LoginSessions.Where(s => s.UserId == user.Id && !s.IsRevoked).ToListAsync();
        foreach (var session in sessions)
        {
            session.IsRevoked = true;
        }

        await _context.SaveChangesAsync();
        await LogAction(user.Id, "FORCED_PASSWORD_RESET_COMPLETED", $"IP: {ip}", ip);

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

    private int? GetCurrentUserIdFromClaims()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    private async Task<bool> HasActiveSessionAsync(int userId)
    {
        var token = Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var tokenHash = _jwtService.HashToken(token);
        var session = await _context.LoginSessions
            .FirstOrDefaultAsync(s => s.Token == tokenHash && s.UserId == userId && !s.IsRevoked);

        if (session == null || session.ExpiresAt < DateTime.UtcNow)
        {
            return false;
        }

        var user = await _context.Users.FindAsync(userId);
        return user != null && user.IsActive && !user.IsBanned;
    }

    private static string HashText(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string GenerateResetCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> randomBytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(randomBytes);
        var result = new char[8];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = chars[randomBytes[i] % chars.Length];
        }
        return new string(result);
    }

    private async Task<bool> HasPendingAdminPasswordResetAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return false;
        }

        if (!user.IsAdminPasswordResetRequired)
        {
            return false;
        }

        if (user.AdminResetCodeExpiresAt == null || user.AdminResetCodeExpiresAt < DateTime.UtcNow)
        {
            user.IsAdminPasswordResetRequired = false;
            user.AdminResetCodeHash = null;
            user.AdminResetCodeExpiresAt = null;
            await _context.SaveChangesAsync();
            return false;
        }

        return true;
    }

    private bool ValidateAdminStepUp(out ActionResult? errorResult)
    {
        var expectedKey = _configuration["AdminKey"];
        var providedKey = Request.Headers["X-Admin-Key"].ToString();

        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            errorResult = StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Admin key is not configured" });
            return false;
        }

        if (!string.Equals(expectedKey, providedKey, StringComparison.Ordinal))
        {
            errorResult = Unauthorized(new { success = false, message = "Invalid admin security key" });
            return false;
        }

        errorResult = null;
        return true;
    }

    private async Task<bool> IsCurrentUserAdminAsync()
    {
        var callerUserId = GetCurrentUserIdFromClaims();
        if (callerUserId == null)
        {
            return false;
        }

        if (!await HasActiveSessionAsync(callerUserId.Value))
        {
            return false;
        }

        var adminUserIds = (_configuration["Admin:UserIds"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => int.TryParse(v, out var id) ? id : -1)
            .Where(id => id > 0)
            .ToHashSet();

        return adminUserIds.Contains(callerUserId.Value);
    }

    [HttpPost("change-username")]
    [Authorize]
    public async Task<ActionResult<AuthResponse>> ChangeUsername([FromBody] ChangeUsernameRequest request)
    {
        var userId = GetCurrentUserIdFromClaims();
        if (userId == null)
        {
            return Unauthorized(new AuthResponse { Success = false, Message = "Недействительный токен" });
        }

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null)
        {
            return NotFound(new AuthResponse { Success = false, Message = "Пользователь не найден" });
        }

        // Проверка нового никнейма
        if (string.IsNullOrWhiteSpace(request.NewUsername) || request.NewUsername.Length < 3 || request.NewUsername.Length > 16)
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Никнейм должен быть от 3 до 16 символов" });
        }

        // Проверка что никнейм не занят
        var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.NewUsername);
        if (existingUser != null)
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Этот никнейм уже занят" });
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await LogAction(userId.Value, "USERNAME_CHANGED", $"Old: {user.Username}, New: {request.NewUsername}", ip);

        user.Username = request.NewUsername;
        _identityConsistency.RepairMinecraftUuidIfMismatch(user);
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new AuthResponse
        {
            Success = true,
            Message = "Никнейм успешно изменен",
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

    [HttpPost("update-playtime")]
    [Authorize]
    public async Task<ActionResult<AuthResponse>> UpdatePlayTime([FromBody] UpdatePlayTimeRequest request)
    {
        var userId = GetCurrentUserIdFromClaims();
        if (userId == null)
        {
            return Unauthorized(new AuthResponse { Success = false, Message = "Недействительный токен" });
        }

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null)
        {
            return NotFound(new AuthResponse { Success = false, Message = "Пользователь не найден" });
        }

        user.PlayTimeMinutes += request.MinutesPlayed;
        user.LastPlayTimeUpdate = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new AuthResponse
        {
            Success = true,
            Message = "Игровое время обновлено"
        });
    }

    [HttpGet("profile")]
    [Authorize]
    public async Task<ActionResult<ProfileResponse>> GetProfile()
    {
        var userId = GetCurrentUserIdFromClaims();
        if (userId == null)
        {
            return Unauthorized(new { success = false, message = "Недействительный токен" });
        }

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null)
        {
            return NotFound(new { success = false, message = "Пользователь не найден" });
        }

        await _identityConsistency.EnsureMinecraftUuidPersistedAsync(user);

        return Ok(new ProfileResponse
        {
            Success = true,
            Username = user.Username,
            Email = user.Email,
            PlayTimeMinutes = user.PlayTimeMinutes,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            RequiresPasswordReset = await HasPendingAdminPasswordResetAsync(user.Id)
        });
    }

    [HttpGet("admin/access")]
    [Authorize]
    public async Task<ActionResult<object>> GetAdminAccess()
    {
        return Ok(new { success = true, isAdmin = await IsCurrentUserAdminAsync() });
    }

    [HttpPost("admin/unlock")]
    [Authorize]
    public async Task<ActionResult<object>> UnlockAdminAccess()
    {
        if (!await IsCurrentUserAdminAsync())
        {
            return Forbid();
        }

        if (!ValidateAdminStepUp(out var errorResult))
        {
            return errorResult!;
        }

        return Ok(new { success = true, isAdmin = true });
    }

    [HttpGet("admin/users")]
    [Authorize]
    public async Task<ActionResult<object>> GetUsersForAdmin()
    {
        if (!ValidateAdminStepUp(out var errorResult))
        {
            return errorResult!;
        }

        if (!await IsCurrentUserAdminAsync())
        {
            return Forbid();
        }

        var usersRaw = await _context.Users
            .OrderBy(u => u.Id)
            .Select(u => new AdminUserResponse
            {
                Id = u.Id,
                Username = u.Username,
                IsActive = u.IsActive,
                IsBanned = u.IsBanned,
                IsWhitelisted = u.IsWhitelisted,
                RequiresPasswordReset = u.IsAdminPasswordResetRequired &&
                    u.AdminResetCodeExpiresAt != null &&
                    u.AdminResetCodeExpiresAt > DateTime.UtcNow,
                LastLoginAt = u.LastLoginAt,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync();

        return Ok(new { success = true, users = usersRaw });
    }

    [HttpPost("admin/reset-password")]
    [Authorize]
    public async Task<ActionResult<AuthResponse>> AdminResetPassword([FromBody] AdminResetPasswordRequest request)
    {
        if (!ValidateAdminStepUp(out var errorResult))
        {
            return errorResult!;
        }

        if (!await IsCurrentUserAdminAsync())
        {
            return Forbid();
        }

        if (request.UserId <= 0)
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Некорректные параметры запроса" });
        }

        var targetUser = await _context.Users.FindAsync(request.UserId);
        if (targetUser == null)
        {
            return NotFound(new AuthResponse { Success = false, Message = "Пользователь не найден" });
        }

        targetUser.UpdatedAt = DateTime.UtcNow;
        targetUser.IsAdminPasswordResetRequired = true;
        var resetCode = GenerateResetCode();
        targetUser.AdminResetCodeHash = HashText(resetCode);
        targetUser.AdminResetCodeExpiresAt = DateTime.UtcNow.AddMinutes(30);

        var sessions = await _context.LoginSessions
            .Where(s => s.UserId == targetUser.Id && !s.IsRevoked)
            .ToListAsync();
        foreach (var session in sessions)
        {
            session.IsRevoked = true;
        }

        await _context.SaveChangesAsync();

        var adminUserId = GetCurrentUserIdFromClaims();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await LogAction(adminUserId, "ADMIN_PASSWORD_RESET_REQUIRED", $"TargetUserId: {targetUser.Id}; Note: {request.Note}", ip);

        return Ok(new AuthResponse
        {
            Success = true,
            Message = "Игроку поставлена принудительная смена пароля. Передайте ему одноразовый код сброса.",
            NotificationMessage = $"Одноразовый код сброса для {targetUser.Username}: {resetCode} (действует 30 минут)"
        });
    }

    [HttpPost("admin/set-ban")]
    [Authorize]
    public async Task<ActionResult<AuthResponse>> AdminSetBan([FromBody] AdminSetBanRequest request)
    {
        if (!ValidateAdminStepUp(out var errorResult))
        {
            return errorResult!;
        }

        if (!await IsCurrentUserAdminAsync())
        {
            return Forbid();
        }

        if (request.UserId <= 0)
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Некорректный UserId" });
        }

        var targetUser = await _context.Users.FindAsync(request.UserId);
        if (targetUser == null)
        {
            return NotFound(new AuthResponse { Success = false, Message = "Пользователь не найден" });
        }

        targetUser.IsBanned = request.IsBanned;
        targetUser.BanReason = request.IsBanned ? (request.BanReason?.Trim() ?? "Заблокирован администратором") : null;
        targetUser.UpdatedAt = DateTime.UtcNow;

        if (request.IsBanned)
        {
            var sessions = await _context.LoginSessions
                .Where(s => s.UserId == targetUser.Id && !s.IsRevoked)
                .ToListAsync();
            foreach (var session in sessions)
            {
                session.IsRevoked = true;
            }
        }

        await _context.SaveChangesAsync();

        var adminUserId = GetCurrentUserIdFromClaims();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await LogAction(adminUserId, "ADMIN_SET_BAN", $"TargetUserId: {targetUser.Id}, IsBanned: {request.IsBanned}", ip);

        return Ok(new AuthResponse
        {
            Success = true,
            Message = request.IsBanned ? "Пользователь заблокирован" : "Блокировка снята"
        });
    }

    [HttpDelete("admin/users/{userId:int}")]
    [Authorize]
    public async Task<ActionResult<AuthResponse>> AdminDeleteUser(int userId)
    {
        if (!ValidateAdminStepUp(out var errorResult))
        {
            return errorResult!;
        }

        if (!await IsCurrentUserAdminAsync())
        {
            return Forbid();
        }

        var adminUserId = GetCurrentUserIdFromClaims();
        if (adminUserId == userId)
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Нельзя удалить собственный аккаунт через админ-панель" });
        }

        var targetUser = await _context.Users.FindAsync(userId);
        if (targetUser == null)
        {
            return NotFound(new AuthResponse { Success = false, Message = "Пользователь не найден" });
        }

        var sessions = await _context.LoginSessions.Where(s => s.UserId == userId).ToListAsync();
        var skins = await _context.PlayerSkins.Where(s => s.UserId == userId).ToListAsync();
        var capes = await _context.PlayerCapes.Where(c => c.UserId == userId).ToListAsync();
        var logs = await _context.AuditLogs.Where(l => l.UserId == userId).ToListAsync();

        if (sessions.Count > 0) _context.LoginSessions.RemoveRange(sessions);
        if (skins.Count > 0) _context.PlayerSkins.RemoveRange(skins);
        if (capes.Count > 0) _context.PlayerCapes.RemoveRange(capes);
        if (logs.Count > 0) _context.AuditLogs.RemoveRange(logs);

        _context.Users.Remove(targetUser);
        await _context.SaveChangesAsync();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await LogAction(adminUserId, "ADMIN_DELETE_USER", $"TargetUserId: {userId}", ip);

        return Ok(new AuthResponse { Success = true, Message = "Пользователь удален" });
    }
}

// Request/Response DTOs
public class ChangeUsernameRequest
{
    public string NewUsername { get; set; } = string.Empty;
}

public class UpdatePlayTimeRequest
{
    public int MinutesPlayed { get; set; }
}

public class AdminResetPasswordRequest
{
    public int UserId { get; set; }
    public string? Note { get; set; }
}

public class AdminSetBanRequest
{
    public int UserId { get; set; }
    public bool IsBanned { get; set; }
    public string? BanReason { get; set; }
}

public class AdminUserResponse
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsBanned { get; set; }
    public bool IsWhitelisted { get; set; }
    public bool RequiresPasswordReset { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProfileResponse
{
    public bool Success { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int PlayTimeMinutes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool RequiresPasswordReset { get; set; }
}
