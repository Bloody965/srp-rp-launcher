using System;
using System.Security.Cryptography;
using System.Text;
using ApocalypseLauncher.Core.Models;

namespace ApocalypseLauncher.Core.Services;

public class AuthService
{
    public AuthResult AuthenticateOffline(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username cannot be empty", nameof(username));

        var uuid = GenerateOfflineUUID(username);
        var accessToken = GenerateAccessToken();

        return new AuthResult
        {
            Username = username,
            UUID = uuid,
            AccessToken = accessToken,
            IsOffline = true,
            CreatedAt = DateTime.Now
        };
    }

    private string GenerateOfflineUUID(string username)
    {
        // Генерация UUID для offline режима (как в оригинальном Minecraft)
        var input = $"OfflinePlayer:{username}";
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));

        // Устанавливаем версию UUID (3) и вариант
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        var guid = new Guid(hash);
        return guid.ToString();
    }

    private string GenerateAccessToken()
    {
        return Guid.NewGuid().ToString("N");
    }
}
