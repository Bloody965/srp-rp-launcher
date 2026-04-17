using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApocalypseLauncher.API.Data;
using ApocalypseLauncher.API.Models;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace ApocalypseLauncher.API.Controllers;

// ОТКЛЮЧЕН - используется SimpleModpackController
/*
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ModpackController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<ModpackController> _logger;
    private readonly IConfiguration _configuration;

    public ModpackController(
        AppDbContext context,
        ILogger<ModpackController> logger,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
    }

    [HttpGet("version")]
    public async Task<ActionResult<ModpackInfoResponse>> GetLatestVersion()
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var user = await _context.Users.FindAsync(userId);
        if (user == null || !user.IsActive || user.IsBanned)
            return Unauthorized(new { message = "Аккаунт недоступен" });

        var latestVersion = await _context.ModpackVersions
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync();

        if (latestVersion == null)
            return NotFound(new { message = "Сборка не найдена" });

        await LogAction(userId.Value, "MODPACK_VERSION_CHECK", $"Version: {latestVersion.Version}");

        return Ok(new ModpackInfoResponse
        {
            Version = latestVersion.Version,
            DownloadUrl = latestVersion.DownloadUrl,
            SHA256Hash = latestVersion.SHA256Hash,
            FileSizeBytes = latestVersion.FileSizeBytes,
            Changelog = latestVersion.Changelog
        });
    }

    [HttpGet("download")]
    public async Task<IActionResult> DownloadModpack()
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var user = await _context.Users.FindAsync(userId);
        if (user == null || !user.IsActive || user.IsBanned)
            return Unauthorized(new { message = "Аккаунт недоступен" });

        // Проверка whitelist (опционально)
        var requireWhitelist = _configuration.GetValue<bool>("Modpack:RequireWhitelist", false);
        if (requireWhitelist && !user.IsWhitelisted)
        {
            return Forbid("Вы не в whitelist сервера");
        }

        var latestVersion = await _context.ModpackVersions
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync();

        if (latestVersion == null)
            return NotFound(new { message = "Сборка не найдена" });

        await LogAction(userId.Value, "MODPACK_DOWNLOAD", $"Version: {latestVersion.Version}");

        _logger.LogInformation($"User {user.Username} (ID: {userId}) downloading modpack version {latestVersion.Version}");

        // Если файл хранится локально
        var modpackPath = _configuration["Modpack:StoragePath"];
        if (!string.IsNullOrEmpty(modpackPath))
        {
            var filePath = Path.Combine(modpackPath, $"{latestVersion.Version}.zip");
            if (System.IO.File.Exists(filePath))
            {
                var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                return File(fileStream, "application/zip", $"modpack-{latestVersion.Version}.zip");
            }
        }

        // Если файл на внешнем хранилище - редирект
        return Redirect(latestVersion.DownloadUrl);
    }

    [HttpPost("verify")]
    public async Task<ActionResult> VerifyModpack([FromBody] VerifyModpackRequest request)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var version = await _context.ModpackVersions
            .FirstOrDefaultAsync(m => m.Version == request.Version && m.IsActive);

        if (version == null)
            return NotFound(new { message = "Версия не найдена" });

        var isValid = version.SHA256Hash.Equals(request.SHA256Hash, StringComparison.OrdinalIgnoreCase);

        await LogAction(userId.Value, "MODPACK_VERIFY",
            $"Version: {request.Version}, Valid: {isValid}");

        return Ok(new
        {
            isValid,
            expectedHash = version.SHA256Hash,
            providedHash = request.SHA256Hash
        });
    }

    private int? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim != null && int.TryParse(userIdClaim, out var userId))
            return userId;
        return null;
    }

    private async Task LogAction(int userId, string action, string? details)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
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

public class VerifyModpackRequest
{
    public string Version { get; set; } = string.Empty;
    public string SHA256Hash { get; set; } = string.Empty;
}
*/
