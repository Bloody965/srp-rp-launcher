using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApocalypseLauncher.API.Data;
using System.Text.Json;

namespace ApocalypseLauncher.API.Controllers;

[ApiController]
[Route("api/yggdrasil")]
public class YggdrasilController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<YggdrasilController> _logger;
    private readonly string _baseUrl;

    public YggdrasilController(
        AppDbContext context,
        ILogger<YggdrasilController> logger,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _baseUrl = configuration["BaseUrl"] ?? "https://srp-rp-launcher-production.up.railway.app";
    }

    // Метаданные сервера аутентификации (корневой endpoint)
    [HttpGet("")]
    public IActionResult GetMetadata()
    {
        var metadata = new
        {
            meta = new
            {
                serverName = "SRP-RP Launcher",
                implementationName = "ApocalypseLauncher",
                implementationVersion = "1.0.0",
                feature = new
                {
                    non_email_login = true,
                    legacy_skin_api = false,
                    no_mojang_namespace = true
                }
            },
            skinDomains = new[] { "srp-rp-launcher-production.up.railway.app" },
            signaturePublickey = (string?)null
        };

        return Ok(metadata);
    }

    // Получить профиль игрока по UUID (используется Minecraft для загрузки скинов)
    [HttpGet("sessionserver/session/minecraft/profile/{uuid}")]
    public async Task<IActionResult> GetProfile(string uuid)
    {
        try
        {
            // Убираем дефисы из UUID если они есть
            uuid = uuid.Replace("-", "");

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.MinecraftUUID.Replace("-", "") == uuid);

            if (user == null)
            {
                return NotFound();
            }

            // Получаем активный скин пользователя
            var skin = await _context.PlayerSkins
                .Where(s => s.UserId == user.Id && s.IsActive)
                .OrderByDescending(s => s.UploadedAt)
                .FirstOrDefaultAsync();

            // Получаем активный плащ
            var cape = await _context.PlayerCapes
                .Where(c => c.UserId == user.Id && c.IsActive)
                .OrderByDescending(c => c.UploadedAt)
                .FirstOrDefaultAsync();

            var textures = new Dictionary<string, object>();

            if (skin != null)
            {
                textures["SKIN"] = new
                {
                    url = $"{_baseUrl}/api/skins/download/{user.Id}",
                    metadata = new
                    {
                        model = skin.SkinType == "slim" ? "slim" : "default"
                    }
                };
            }

            if (cape != null)
            {
                textures["CAPE"] = new
                {
                    url = $"{_baseUrl}/api/skins/capes/download/{user.Id}"
                };
            }

            var texturesPayload = new
            {
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                profileId = user.MinecraftUUID.Replace("-", ""),
                profileName = user.Username,
                textures = textures
            };

            var texturesJson = JsonSerializer.Serialize(texturesPayload);
            var texturesBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(texturesJson));

            var profile = new
            {
                id = user.MinecraftUUID.Replace("-", ""),
                name = user.Username,
                properties = new[]
                {
                    new
                    {
                        name = "textures",
                        value = texturesBase64
                    }
                }
            };

            _logger.LogInformation($"Profile requested for UUID {uuid} (user: {user.Username})");

            return Ok(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting profile for UUID {uuid}: {ex.Message}");
            return StatusCode(500);
        }
    }

    // Получить UUID по имени пользователя
    [HttpGet("api/profiles/minecraft/{username}")]
    public async Task<IActionResult> GetUuidByUsername(string username)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

            if (user == null)
            {
                return NotFound();
            }

            var profile = new
            {
                id = user.MinecraftUUID.Replace("-", ""),
                name = user.Username
            };

            return Ok(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting UUID for username {username}: {ex.Message}");
            return StatusCode(500);
        }
    }

    // Batch UUID lookup (опционально, для оптимизации)
    [HttpPost("api/profiles/minecraft")]
    public async Task<IActionResult> GetUuidsByUsernames([FromBody] string[] usernames)
    {
        try
        {
            if (usernames == null || usernames.Length == 0)
            {
                return BadRequest();
            }

            var lowerUsernames = usernames.Select(u => u.ToLower()).ToList();

            var users = await _context.Users
                .Where(u => lowerUsernames.Contains(u.Username.ToLower()))
                .Select(u => new
                {
                    id = u.MinecraftUUID.Replace("-", ""),
                    name = u.Username
                })
                .ToListAsync();

            return Ok(users);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in batch UUID lookup: {ex.Message}");
            return StatusCode(500);
        }
    }
}
