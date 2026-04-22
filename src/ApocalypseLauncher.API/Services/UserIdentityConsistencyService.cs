using ApocalypseLauncher.API.Data;
using ApocalypseLauncher.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ApocalypseLauncher.API.Services;

/// <summary>
/// Страховка от рассинхрона MinecraftUUID и Username (ломает Yggdrasil profile и скины).
/// </summary>
public class UserIdentityConsistencyService
{
    private readonly AppDbContext _db;
    private readonly PasswordService _passwordService;
    private readonly ILogger<UserIdentityConsistencyService> _logger;

    public UserIdentityConsistencyService(
        AppDbContext db,
        PasswordService passwordService,
        ILogger<UserIdentityConsistencyService> logger)
    {
        _db = db;
        _passwordService = passwordService;
        _logger = logger;
    }

    /// <summary>Офлайн-UUID (Java) для ника, без дефисов — единый источник с лаунчером / Yggdrasil.</summary>
    public string OfflineUuidNoDashesForUsername(string username)
        => _passwordService.GenerateMinecraftUUID(username).Replace("-", "", StringComparison.Ordinal);

    /// <summary>Исправляет сущность в памяти; сохранение — отдельно (один SaveChanges в контроллере).</summary>
    public bool RepairMinecraftUuidIfMismatch(User user)
    {
        var expected = _passwordService.GenerateMinecraftUUID(user.Username);
        if (string.Equals(user.MinecraftUUID, expected, StringComparison.OrdinalIgnoreCase))
            return false;

        user.MinecraftUUID = expected;
        user.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>Сохраняет исправление сразу (Yggdrasil и др. без общего UnitOfWork).</summary>
    public async Task EnsureMinecraftUuidPersistedAsync(User user, CancellationToken cancellationToken = default)
    {
        if (!RepairMinecraftUuidIfMismatch(user))
            return;

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogWarning(
            "Исправлено расхождение MinecraftUUID для пользователя {UserId} ({Username}).",
            user.Id,
            user.Username);
    }
}
