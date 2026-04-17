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

            // Сохраняем файл
            var fileName = $"{userId}_{DateTime.UtcNow.Ticks}.png";
            var filePath = Path.Combine(_storageBasePath, "skins", fileName);

            stream.Position = 0;
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await stream.CopyToAsync(fileStream);
            }

            // Создаем запись в БД
            var playerSkin = new PlayerSkin
            {
                UserId = userId.Value,
                SkinType = skinType,
                FileName = fileName,
                FileHash = fileHash,
                FileSizeBytes = skin.Length,
                UploadedAt = DateTime.UtcNow,
                IsActive = true
            };

            _context.PlayerSkins.Add(playerSkin);
            await _context.SaveChangesAsync();

            await LogAction(userId.Value, "SKIN_UPLOADED", $"Type: {skinType}, Size: {skin.Length} bytes", ip);

            _logger.LogInformation($"User {userId} uploaded skin: {fileName}");

            return Ok(new
            {
                success = true,
                message = "Скин успешно загружен",
                skin = new
                {
                    skinType = playerSkin.SkinType,
                    downloadUrl = $"/api/skins/download/{userId}",
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
                downloadUrl = $"/api/skins/download/{userId}",
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
                downloadUrl = $"/api/skins/download/{userId}",
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

        var filePath = Path.Combine(_storageBasePath, "skins", skin.FileName);

        if (!System.IO.File.Exists(filePath))
        {
            _logger.LogWarning($"Skin file not found: {filePath}");
            return NotFound();
        }

        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        return File(fileStream, "image/png", $"skin_{userId}.png");
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
            // Деактивируем скин
            skin.IsActive = false;
            await _context.SaveChangesAsync();

            // Удаляем файл
            var filePath = Path.Combine(_storageBasePath, "skins", skin.FileName);
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }

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

            // Сохраняем файл
            var fileName = $"{userId}_{DateTime.UtcNow.Ticks}.png";
            var filePath = Path.Combine(_storageBasePath, "capes", fileName);

            stream.Position = 0;
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await stream.CopyToAsync(fileStream);
            }

            // Создаем запись в БД
            var playerCape = new PlayerCape
            {
                UserId = userId.Value,
                FileName = fileName,
                FileHash = fileHash,
                FileSizeBytes = cape.Length,
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

        var filePath = Path.Combine(_storageBasePath, "capes", cape.FileName);

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound();
        }

        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        return File(fileStream, "image/png", $"cape_{userId}.png");
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

    private async Task<string> CalculateSHA256Async(Stream stream)
    {
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
