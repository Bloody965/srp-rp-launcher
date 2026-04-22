using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace ApocalypseLauncher.API.Services;

/// <summary>
/// Одноразовые коды для входа в лаунчер после авторизации на сайте (без передачи JWT в URL).
/// В кэше хранится только HMAC от нормализованного кода (не сам код), чтобы дамп памяти не раскрывал активные коды.
/// </summary>
public class WebHandoffService
{
    private readonly IMemoryCache _cache;
    private readonly byte[] _hmacKey;
    private const string Prefix = "handoff:";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    public WebHandoffService(IMemoryCache cache, byte[] webHandoffHmacKey)
    {
        _cache = cache;
        _hmacKey = webHandoffHmacKey ?? throw new ArgumentNullException(nameof(webHandoffHmacKey));
        if (_hmacKey.Length < 32)
        {
            throw new ArgumentException("Handoff HMAC key must be at least 32 bytes.", nameof(webHandoffHmacKey));
        }
    }

    public bool TryCreate(int userId, out string code)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            code = ToUrlSafeBase64(RandomNumberGenerator.GetBytes(24));
            var cacheKey = Prefix + HashNormalized(Normalize(code));
            if (_cache.TryGetValue(cacheKey, out _))
            {
                continue;
            }

            _cache.Set(cacheKey, userId, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = Ttl
            });
            return true;
        }

        code = string.Empty;
        return false;
    }

    public bool TryConsume(string rawCode, out int userId)
    {
        userId = 0;
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return false;
        }

        var cacheKey = Prefix + HashNormalized(Normalize(rawCode));
        if (!_cache.TryGetValue(cacheKey, out int uid))
        {
            return false;
        }

        _cache.Remove(cacheKey);
        userId = uid;
        return true;
    }

    private string HashNormalized(string normalizedCode)
    {
        using var hmac = new HMACSHA256(_hmacKey);
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalizedCode));
        return Convert.ToHexString(digest);
    }

    private static string Normalize(string c) => c.Trim().ToLowerInvariant();

    private static string ToUrlSafeBase64(byte[] data)
    {
        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
