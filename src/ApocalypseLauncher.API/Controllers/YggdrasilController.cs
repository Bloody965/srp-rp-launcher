using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApocalypseLauncher.API.Data;
using System.Text.Json;
using System.Security.Claims;
using ApocalypseLauncher.API.Services;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

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
    private readonly YggdrasilSignatureService _signatureService;

    private static readonly TimeSpan JoinSessionTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan UuidLookupTtl = TimeSpan.FromHours(24);

    public YggdrasilController(
        AppDbContext context,
        ILogger<YggdrasilController> logger,
        IConfiguration configuration,
        JwtService jwtService,
        IMemoryCache cache,
        YggdrasilSignatureService signatureService)
    {
        _context = context;
        _logger = logger;
        _baseUrl = configuration["BaseUrl"] ?? "https://srp-rp-launcher-production.up.railway.app";
        _jwtService = jwtService;
        _cache = cache;
        _signatureService = signatureService;
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
            signaturePublickey = _signatureService.PublicKeyBase64
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
                // Offline-mode fallback:
                // UUID->Username is not reversible, so we cache uuid->username when the game calls
                // /api/profiles/minecraft/{username}. This allows skins to work in offline servers
                // even if DB UUID differs (e.g. due to nickname casing).
                var uuidCacheKey = BuildUuidCacheKey(uuid);
                if (_cache.TryGetValue(uuidCacheKey, out string? cachedUsername) && !string.IsNullOrWhiteSpace(cachedUsername))
                {
                    user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == cachedUsername.ToLower());
                }

                if (user == null)
                {
                    return NotFound();
                }
            }

            if (user.IsBanned || !user.IsActive)
            {
                return NotFound();
            }

            var profile = await BuildProfileResponseAsync(user, uuid);

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

            var accessTokenHash = _jwtService.HashToken(request.AccessToken);
            var session = await _context.LoginSessions
                .FirstOrDefaultAsync(s => s.UserId == user.Id && s.Token == accessTokenHash && !s.IsRevoked);
            if (session == null || session.ExpiresAt < DateTime.UtcNow)
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
            if (user == null || user.IsBanned || !user.IsActive)
            {
                return NotFound();
            }

            // Extra safety: ensure cached UUID matches DB user.
            if (!string.Equals(user.MinecraftUUID.Replace("-", ""), userUuid.Replace("-", ""), StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }

            var profile = await BuildProfileResponseAsync(user, userUuid.Replace("-", ""));
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

    private static string BuildUuidCacheKey(string uuidNoDashes)
        => $"uuidmap:{uuidNoDashes.Trim().ToLowerInvariant()}";

    private static string GenerateOfflineUuid(string username)
    {
        // Vanilla offline UUID algorithm: nameUUIDFromBytes(("OfflinePlayer:" + name).getBytes("UTF-8"))
        var input = $"OfflinePlayer:{username}";
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return FormatUuidJavaStyle(hash);
    }

    private static string FormatUuidJavaStyle(byte[] bytes)
    {
        // Java UUID uses network byte order; .NET Guid(byte[]) reorders bytes.
        return string.Create(36, bytes, static (span, src) =>
        {
            var hex = "0123456789abcdef";
            var j = 0;
            for (var i = 0; i < 16; i++)
            {
                if (i == 4 || i == 6 || i == 8 || i == 10)
                {
                    span[j++] = '-';
                }

                var b = src[i];
                span[j++] = hex[b >> 4];
                span[j++] = hex[b & 0x0F];
            }
        });
    }

    private async Task<object> BuildProfileResponseAsync(Models.User user, string? profileIdOverride = null)
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

        var effectiveProfileId = string.IsNullOrWhiteSpace(profileIdOverride)
            ? user.MinecraftUUID.Replace("-", "")
            : profileIdOverride.Replace("-", "");

        var texturesPayload = new
        {
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            profileId = effectiveProfileId,
            profileName = user.Username,
            textures = textures
        };

        var texturesJson = JsonSerializer.Serialize(texturesPayload);
        var texturesBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(texturesJson));

        var signatureBase64 = _signatureService.SignTextures(texturesBase64);

        return new
        {
            id = effectiveProfileId,
            name = user.Username,
            properties = new[]
            {
                new
                {
                    name = "textures",
                    value = texturesBase64,
                    signature = signatureBase64
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
            if (string.IsNullOrWhiteSpace(username))
            {
                return BadRequest();
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

            if (user == null || user.IsBanned || !user.IsActive)
            {
                return NotFound();
            }

            // Offline-mode compatibility:
            // Return the offline UUID for the requested username and cache uuid->username,
            // so /profile/{uuid} can resolve and return textures.
            var offlineUuid = GenerateOfflineUuid(username).Replace("-", "");
            _cache.Set(BuildUuidCacheKey(offlineUuid), username, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = UuidLookupTtl
            });

            var profile = new
            {
                id = offlineUuid,
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
                .Where(u => u.IsActive && !u.IsBanned)
                .Select(u => new
                {
                    name = u.Username
                })
                .ToListAsync();

            var result = new List<object>(users.Count);
            foreach (var u in users)
            {
                var requested = usernames.FirstOrDefault(x => string.Equals(x, u.name, StringComparison.OrdinalIgnoreCase)) ?? u.name;
                var offlineUuid = GenerateOfflineUuid(requested).Replace("-", "");
                _cache.Set(BuildUuidCacheKey(offlineUuid), requested, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = UuidLookupTtl
                });

                result.Add(new { id = offlineUuid, name = u.name });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in batch UUID lookup: {ex.Message}");
            return StatusCode(500);
        }
    }
}
