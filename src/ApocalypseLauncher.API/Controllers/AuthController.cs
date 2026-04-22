using Microsoft.AspNetCore.Authorization;
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
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppDbContext context,
        JwtService jwtService,
        PasswordService passwordService,
        RateLimitService rateLimitService,
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _passwordService = passwordService;
        _rateLimitService = rateLimitService;
        _configuration = configuration;
        _logger = logger;
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

        return Ok(new ProfileResponse
        {
            Success = true,
            Username = user.Username,
            Email = user.Email,
            PlayTimeMinutes = user.PlayTimeMinutes,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt
        });
    }

    [HttpGet("admin/access")]
    [Authorize]
    public async Task<ActionResult<object>> GetAdminAccess()
    {
        return Ok(new { success = true, isAdmin = await IsCurrentUserAdminAsync() });
    }

    [HttpGet("admin/users")]
    [Authorize]
    public async Task<ActionResult<object>> GetUsersForAdmin()
    {
        if (!await IsCurrentUserAdminAsync())
        {
            return Forbid();
        }

        var users = await _context.Users
            .OrderBy(u => u.Id)
            .Select(u => new AdminUserResponse
            {
                Id = u.Id,
                Username = u.Username,
                IsActive = u.IsActive,
                IsBanned = u.IsBanned,
                IsWhitelisted = u.IsWhitelisted,
                LastLoginAt = u.LastLoginAt,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync();

        return Ok(new { success = true, users });
    }

    [HttpPost("admin/reset-password")]
    [Authorize]
    public async Task<ActionResult<AuthResponse>> AdminResetPassword([FromBody] AdminResetPasswordRequest request)
    {
        if (!await IsCurrentUserAdminAsync())
        {
            return Forbid();
        }

        if (request.UserId <= 0 || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new AuthResponse { Success = false, Message = "Некорректные параметры запроса" });
        }

        var (isValid, error) = _passwordService.ValidatePasswordStrength(request.NewPassword);
        if (!isValid)
        {
            return BadRequest(new AuthResponse { Success = false, Message = error });
        }

        var targetUser = await _context.Users.FindAsync(request.UserId);
        if (targetUser == null)
        {
            return NotFound(new AuthResponse { Success = false, Message = "Пользователь не найден" });
        }

        targetUser.PasswordHash = _passwordService.HashPassword(request.NewPassword);
        targetUser.UpdatedAt = DateTime.UtcNow;

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
        await LogAction(adminUserId, "ADMIN_PASSWORD_RESET", $"TargetUserId: {targetUser.Id}", ip);

        return Ok(new AuthResponse { Success = true, Message = "Пароль пользователя сброшен и все сессии отозваны" });
    }

    [HttpPost("admin/set-ban")]
    [Authorize]
    public async Task<ActionResult<AuthResponse>> AdminSetBan([FromBody] AdminSetBanRequest request)
    {
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
    public string NewPassword { get; set; } = string.Empty;
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
}
