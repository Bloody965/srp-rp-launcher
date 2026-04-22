using System;

namespace ApocalypseLauncher.Core.Models;

public class AuthResult
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string MinecraftUUID { get; set; } = string.Empty;
    public string UUID { get; set; } = string.Empty; // Для обратной совместимости
    public string AccessToken { get; set; } = string.Empty;
    public bool IsOffline { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? RecoveryCode { get; set; } // Код восстановления (только при регистрации)
    public bool RequiresPasswordReset { get; set; }
    public string? NotificationMessage { get; set; }
}
