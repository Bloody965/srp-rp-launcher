using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApocalypseLauncher.API.Data;
using ApocalypseLauncher.API.Models;
using ApocalypseLauncher.API.Services;
using System.Security.Claims;
using System.Security.Cryptography;

namespace ApocalypseLauncher.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SkinsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly SkinValidationService _validationService;
    private readonly RateLimitService _rateLimitService;
    private readonly ILogger<SkinsController> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _storageBasePath;

    public SkinsController(
        AppDbContext context,
        SkinValidationService validationService,
        RateLimitService rateLimitService,
        ILogger<SkinsController> logger,
        IConfiguration configuration)
    {
        _context = context;
        _validationService = validationService;
        _rateLimitService = rateLimitService;
        _logger = logger;
        _configuration = configuration;

        // Путь для хранения файлов
        _storageBasePath = _configuration["Storage:BasePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "storage");
        Directory.CreateDirectory(Path.Combine(_storageBasePath, "skins"));
        Directory.CreateDirectory(Path.Combine(_storageBasePath, "capes"));
    }

    [HttpPost("upload")]
    public async Task<ActionResult> UploadSkin([FromForm] IFormFile skin, [FromForm] string skinType = "classic")
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Rate limiting - 5 загрузок в час
        if (_rateLimitService.IsRateLimited($"skin_upload:{userId}", 5, TimeSpan.FromHours(1)))
        {
            await LogAction(userId.Value, "SKIN_UPLOAD_RATE_LIMITED", null, ip);
            return BadRequest(new { success = false, message = "Слишком много попыток загрузки. Попробуйте через час." });
        }

        // Валидация типа скина
        if (!_validationService.IsValidSkinType(skinType))
        {
            return BadRequest(new { success = false, message = "Неверный тип скина. Используйте 'classic' или 'slim'." });
        }

        // Проверка файла
        if (skin == null || skin.Length == 0)
        {
            return BadRequest(new { success = false, message = "Файл скина не предоставлен" });
        }

        // Валидация скина
        using var stream = skin.OpenReadStream();
        var (isValid, error) = _validationService.ValidateSkinFile(stream, skin.Length);

        if (!isValid)
        {
            await LogAction(userId.Value, "SKIN_UPLOAD_INVALID", error, ip);
            return BadRequest(new { success = false, message = error });
        }

        try
        {
            // Вычисляем SHA256 хеш
            stream.Position = 0;
            var fileHash = await CalculateSHA256Async(stream);

            // Деактивируем старые скины пользователя
            var oldSkins = await _context.PlayerSkins
                .Where(s => s.UserId == userId.Value && s.IsActive)
                .ToListAsync();

            foreach (var oldSkin in oldSkins)
            {
                oldSkin.IsActive = false;
            }

            // Читаем файл в память
            stream.Position = 0;
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            var fileData = memoryStream.ToArray();

            // Создаем запись в БД с данными файла
            var playerSkin = new PlayerSkin
            {
                UserId = userId.Value,
                SkinType = skinType,
                FileName = $"{userId}_{DateTime.UtcNow.Ticks}.png",
                FileHash = fileHash,
                FileSizeBytes = skin.Length,
                FileData = fileData,
                UploadedAt = DateTime.UtcNow,
                IsActive = true
            };

            _context.PlayerSkins.Add(playerSkin);
            await _context.SaveChangesAsync();

            await LogAction(userId.Value, "SKIN_UPLOADED", $"Type: {skinType}, Size: {skin.Length} bytes", ip);

            _logger.LogInformation($"User {userId} uploaded skin to database: {playerSkin.FileName}");

            return Ok(new
            {
                success = true,
                message = "Скин успешно загружен",
                skin = new
                {
                    skinType = playerSkin.SkinType,
                    downloadUrl = $"/api/skins/download/{userId}?v={playerSkin.FileHash}",
                    fileHash = playerSkin.FileHash,
                    uploadedAt = playerSkin.UploadedAt
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error uploading skin for user {userId}: {ex.Message}");
            await LogAction(userId.Value, "SKIN_UPLOAD_ERROR", ex.Message, ip);
            return StatusCode(500, new { success = false, message = "Ошибка сохранения скина" });
        }
    }

    [HttpGet("current")]
    public async Task<ActionResult> GetCurrentSkin()
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var skin = await _context.PlayerSkins
            .Where(s => s.UserId == userId.Value && s.IsActive)
            .OrderByDescending(s => s.UploadedAt)
            .FirstOrDefaultAsync();

        if (skin == null)
        {
            return NotFound(new { success = false, message = "Скин не найден" });
        }

        return Ok(new
        {
            success = true,
            skin = new
            {
                skinType = skin.SkinType,
                downloadUrl = $"/api/skins/download/{userId}?v={skin.FileHash}",
                fileHash = skin.FileHash,
                uploadedAt = skin.UploadedAt
            }
        });
    }

    [HttpGet("{userId}")]
    [AllowAnonymous]
    public async Task<ActionResult> GetSkinInfo(int userId)
    {
        var skin = await _context.PlayerSkins
            .Where(s => s.UserId == userId && s.IsActive)
            .OrderByDescending(s => s.UploadedAt)
            .FirstOrDefaultAsync();

        if (skin == null)
        {
            return NotFound(new { success = false, message = "Скин не найден" });
        }

        return Ok(new
        {
            success = true,
            skin = new
            {
                skinType = skin.SkinType,
                downloadUrl = $"/api/skins/download/{userId}?v={skin.FileHash}",
                fileHash = skin.FileHash,
                uploadedAt = skin.UploadedAt
            }
        });
    }

    [HttpGet("download/{userId}")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadSkin(int userId)
    {
        var skin = await _context.PlayerSkins
            .Where(s => s.UserId == userId && s.IsActive)
            .OrderByDescending(s => s.UploadedAt)
            .FirstOrDefaultAsync();

        if (skin == null)
        {
            return NotFound();
        }

        if (skin.FileData == null || skin.FileData.Length == 0)
        {
            _logger.LogWarning($"Skin data is empty for user {userId}");
            return NotFound();
        }

        var etag = $"\"{skin.FileHash}\"";
        Response.Headers.ETag = etag;
        // Скин может меняться в любой момент, поэтому заставляем клиентов ре-валидировать кеш.
        Response.Headers.CacheControl = "private, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";

        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrWhiteSpace(ifNoneMatch) && ifNoneMatch.Contains(etag, StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return File(skin.FileData, "image/png", $"skin_{userId}.png");
    }

    [HttpDelete("current")]
    public async Task<ActionResult> DeleteCurrentSkin()
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var skin = await _context.PlayerSkins
            .Where(s => s.UserId == userId.Value && s.IsActive)
            .FirstOrDefaultAsync();

        if (skin == null)
        {
            return NotFound(new { success = false, message = "Активный скин не найден" });
        }

        try
        {
            // Деактивируем скин (данные остаются в БД)
            skin.IsActive = false;
            await _context.SaveChangesAsync();

            await LogAction(userId.Value, "SKIN_DELETED", null, ip);

            return Ok(new { success = true, message = "Скин удален" });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error deleting skin for user {userId}: {ex.Message}");
            return StatusCode(500, new { success = false, message = "Ошибка удаления скина" });
        }
    }

    // Cape endpoints
    [HttpPost("capes/upload")]
    public async Task<ActionResult> UploadCape([FromForm] IFormFile cape)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Rate limiting
        if (_rateLimitService.IsRateLimited($"cape_upload:{userId}", 5, TimeSpan.FromHours(1)))
        {
            await LogAction(userId.Value, "CAPE_UPLOAD_RATE_LIMITED", null, ip);
            return BadRequest(new { success = false, message = "Слишком много попыток загрузки. Попробуйте через час." });
        }

        if (cape == null || cape.Length == 0)
        {
            return BadRequest(new { success = false, message = "Файл плаща не предоставлен" });
        }

        // Валидация плаща
        using var stream = cape.OpenReadStream();
        var (isValid, error) = _validationService.ValidateCapeFile(stream, cape.Length);

        if (!isValid)
        {
            await LogAction(userId.Value, "CAPE_UPLOAD_INVALID", error, ip);
            return BadRequest(new { success = false, message = error });
        }

        try
        {
            stream.Position = 0;
            var fileHash = await CalculateSHA256Async(stream);

            // Деактивируем старые плащи
            var oldCapes = await _context.PlayerCapes
                .Where(c => c.UserId == userId.Value && c.IsActive)
                .ToListAsync();

            foreach (var oldCape in oldCapes)
            {
                oldCape.IsActive = false;
            }

            // Читаем файл в память
            stream.Position = 0;
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            var fileData = memoryStream.ToArray();

            // Создаем запись в БД с данными файла
            var playerCape = new PlayerCape
            {
                UserId = userId.Value,
                FileName = $"{userId}_{DateTime.UtcNow.Ticks}.png",
                FileHash = fileHash,
                FileSizeBytes = cape.Length,
                FileData = fileData,
                UploadedAt = DateTime.UtcNow,
                IsActive = true
            };

            _context.PlayerCapes.Add(playerCape);
            await _context.SaveChangesAsync();

            await LogAction(userId.Value, "CAPE_UPLOADED", $"Size: {cape.Length} bytes", ip);

            return Ok(new
            {
                success = true,
                message = "Плащ успешно загружен",
                cape = new
                {
                    downloadUrl = $"/api/skins/capes/download/{userId}",
                    fileHash = playerCape.FileHash,
                    uploadedAt = playerCape.UploadedAt
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error uploading cape for user {userId}: {ex.Message}");
            await LogAction(userId.Value, "CAPE_UPLOAD_ERROR", ex.Message, ip);
            return StatusCode(500, new { success = false, message = "Ошибка сохранения плаща" });
        }
    }

    [HttpGet("capes/download/{userId}")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadCape(int userId)
    {
        var cape = await _context.PlayerCapes
            .Where(c => c.UserId == userId && c.IsActive)
            .OrderByDescending(c => c.UploadedAt)
            .FirstOrDefaultAsync();

        if (cape == null)
        {
            return NotFound();
        }

        if (cape.FileData == null || cape.FileData.Length == 0)
        {
            _logger.LogWarning($"Cape data is empty for user {userId}");
            return NotFound();
        }

        var etag = $"\"{cape.FileHash}\"";
        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "public, max-age=3600";

        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrWhiteSpace(ifNoneMatch) && ifNoneMatch.Contains(etag, StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return File(cape.FileData, "image/png", $"cape_{userId}.png");
    }

    private int? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim != null && int.TryParse(userIdClaim, out var userId))
            return userId;
        return null;
    }

    private async Task LogAction(int userId, string action, string? details, string ip)
    {
        try
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
        catch (Exception ex)
        {
            // Игнорируем ошибки логирования чтобы не ломать основной функционал
            Console.WriteLine($"[LogAction] Failed to log action: {ex.Message}");
        }
    }

    private async Task<string> CalculateSHA256Async(Stream stream)
    {
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    [HttpPost("admin/reset-rate-limit/{userId}")]
    [Authorize]
    public ActionResult ResetRateLimit(int userId, [FromQuery] string adminKey)
    {
        // Admin endpoint: requires authenticated user and configured admin key.
        // Key check is kept for operational compatibility, but no insecure fallback.
        var expectedKey = _configuration["AdminKey"];
        if (string.IsNullOrWhiteSpace(expectedKey) || !string.Equals(adminKey, expectedKey, StringComparison.Ordinal))
        {
            return Unauthorized(new { success = false, message = "Invalid admin key" });
        }

        var callerUserId = GetUserId();
        var adminUserIds = (_configuration["Admin:UserIds"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => int.TryParse(v, out var id) ? id : -1)
            .Where(id => id > 0)
            .ToHashSet();

        if (callerUserId == null || !adminUserIds.Contains(callerUserId.Value))
        {
            return Forbid();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        _rateLimitService.ResetLimit($"skin_upload_{userId}_{ip}");
        _rateLimitService.ResetLimit($"skin_upload_{userId}");

        return Ok(new { success = true, message = $"Rate limit reset for user {userId}" });
    }
}
