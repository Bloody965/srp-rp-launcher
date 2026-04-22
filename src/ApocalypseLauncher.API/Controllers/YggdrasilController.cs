using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApocalypseLauncher.API.Data;
using System.Text.Json;
using System.Security.Claims;
using ApocalypseLauncher.API.Services;
using Microsoft.Extensions.Caching.Memory;

namespace ApocalypseLauncher.API.Controllers;

[ApiController]
[Route("api/yggdrasil")]
public class YggdrasilController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<YggdrasilController> _logger;
    private readonly string _baseUrl;
    private readonly JwtService _jwtService;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan JoinSessionTtl = TimeSpan.FromMinutes(5);

    public YggdrasilController(
        AppDbContext context,
        ILogger<YggdrasilController> logger,
        IConfiguration configuration,
        JwtService jwtService,
        IMemoryCache cache)
    {
        _context = context;
        _logger = logger;
        _baseUrl = configuration["BaseUrl"] ?? "https://srp-rp-launcher-production.up.railway.app";
        _jwtService = jwtService;
        _cache = cache;
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

            var profile = await BuildProfileResponseAsync(user);

            _logger.LogInformation($"Profile requested for UUID {uuid} (user: {user.Username})");

            return Ok(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting profile for UUID {uuid}: {ex.Message}");
            return StatusCode(500);
        }
    }

    // --- sessionserver handshake (required for multiplayer) ---
    public sealed class JoinRequest
    {
        public string AccessToken { get; set; } = string.Empty;
        public string SelectedProfile { get; set; } = string.Empty;
        public string ServerId { get; set; } = string.Empty;
    }

    [HttpPost("sessionserver/session/minecraft/join")]
    public async Task<IActionResult> Join([FromBody] JoinRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.AccessToken) ||
                string.IsNullOrWhiteSpace(request.SelectedProfile) ||
                string.IsNullOrWhiteSpace(request.ServerId))
            {
                return BadRequest();
            }

            var principal = _jwtService.ValidateToken(request.AccessToken);
            var userIdClaim = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized();
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null || !user.IsActive || user.IsBanned)
            {
                return Unauthorized();
            }

            var selectedProfile = request.SelectedProfile.Replace("-", "").Trim();
            var expectedProfile = user.MinecraftUUID.Replace("-", "").Trim();
            if (!string.Equals(selectedProfile, expectedProfile, StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized();
            }

            var cacheKey = BuildJoinCacheKey(request.ServerId, user.Username);
            _cache.Set(cacheKey, user.MinecraftUUID, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = JoinSessionTtl
            });

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in join: {ex.Message}");
            return StatusCode(500);
        }
    }

    [HttpGet("sessionserver/session/minecraft/hasJoined")]
    public async Task<IActionResult> HasJoined([FromQuery] string username, [FromQuery] string serverId, [FromQuery] string? ip = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(serverId))
            {
                return BadRequest();
            }

            var cacheKey = BuildJoinCacheKey(serverId, username);
            if (!_cache.TryGetValue(cacheKey, out string? userUuid) || string.IsNullOrWhiteSpace(userUuid))
            {
                return NotFound();
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
            if (user == null)
            {
                return NotFound();
            }

            // Extra safety: ensure cached UUID matches DB user.
            if (!string.Equals(user.MinecraftUUID.Replace("-", ""), userUuid.Replace("-", ""), StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }

            var profile = await BuildProfileResponseAsync(user);
            return Ok(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in hasJoined: {ex.Message}");
            return StatusCode(500);
        }
    }

    private static string BuildJoinCacheKey(string serverId, string username)
        => $"join:{serverId.Trim()}:{username.Trim().ToLowerInvariant()}";

    private async Task<object> BuildProfileResponseAsync(Models.User user)
    {
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

        return new
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
