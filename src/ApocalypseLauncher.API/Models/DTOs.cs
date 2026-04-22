namespace ApocalypseLauncher.API.Models;

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string Username { get; set; } = string.Empty;
    public string RecoveryCode { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ResetPasswordByAdminRequest
{
    public string Username { get; set; } = string.Empty;
    public string ResetCode { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class AuthResponse
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? Message { get; set; }
    public UserInfo? User { get; set; }
    public string? RecoveryCode { get; set; } // Возвращается только при регистрации
    public bool RequiresPasswordReset { get; set; }
    public string? NotificationMessage { get; set; }
}

public class UserInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string MinecraftUUID { get; set; } = string.Empty;
    public bool IsWhitelisted { get; set; }
}

public class ModpackInfoResponse
{
    public string Version { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string SHA256Hash { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? Changelog { get; set; }
}
