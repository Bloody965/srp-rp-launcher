using System;
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;

namespace ApocalypseLauncher.API.Services;

public class PasswordService
{
    // Bcrypt автоматически генерирует соль и использует 12 раундов по умолчанию
    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    }

    public string HashRecoveryCode(string recoveryCode)
    {
        return BCrypt.Net.BCrypt.HashPassword(recoveryCode, workFactor: 12);
    }

    public bool VerifyPassword(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            return false;
        }
    }

    public bool VerifyRecoveryCode(string recoveryCode, string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(recoveryCode, hash);
        }
        catch
        {
            return false;
        }
    }

    // Генерация UUID для Minecraft (offline mode)
    public string GenerateMinecraftUUID(string username)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes($"OfflinePlayer:{username}"));

        // Устанавливаем версию UUID (3) и вариант
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        return FormatUuidJavaStyle(hash);
    }

    private static string FormatUuidJavaStyle(byte[] bytes)
    {
        return string.Create(36, bytes, static (span, src) =>
        {
            const string hex = "0123456789abcdef";
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

    // Проверка сложности пароля
    public (bool IsValid, string? Error) ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return (false, "Пароль не может быть пустым");

        if (password.Length < 8)
            return (false, "Пароль должен содержать минимум 8 символов");

        if (password.Length > 128)
            return (false, "Пароль слишком длинный (максимум 128 символов)");

        bool hasUpper = false;
        bool hasLower = false;
        bool hasDigit = false;

        foreach (char c in password)
        {
            if (char.IsUpper(c)) hasUpper = true;
            if (char.IsLower(c)) hasLower = true;
            if (char.IsDigit(c)) hasDigit = true;
        }

        if (!hasUpper)
            return (false, "Пароль должен содержать хотя бы одну заглавную букву");

        if (!hasLower)
            return (false, "Пароль должен содержать хотя бы одну строчную букву");

        if (!hasDigit)
            return (false, "Пароль должен содержать хотя бы одну цифру");

        return (true, null);
    }

    // Генерация кода восстановления (16 символов: буквы и цифры)
    public string GenerateRecoveryCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(random);
        }

        var result = new char[16];
        for (int i = 0; i < 16; i++)
        {
            result[i] = chars[random[i] % chars.Length];
        }

        return new string(result);
    }
}
