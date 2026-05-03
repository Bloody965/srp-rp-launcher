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
    /// <summary>Только для локального offline-входа без JWT. Для API-сессий всегда false — не полагаться на дефолт при десериализации.</summary>
    public bool IsOffline { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? RecoveryCode { get; set; } // Код восстановления (только при регистрации)
    public bool RequiresPasswordReset { get; set; }
    public string? NotificationMessage { get; set; }
}
