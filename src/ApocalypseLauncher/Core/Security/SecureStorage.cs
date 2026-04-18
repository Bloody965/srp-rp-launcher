using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ApocalypseLauncher.Core.Security;

/// <summary>
/// Кроссплатформенное шифрование данных
/// Использует AES-256 с ключом, производным от машины
/// </summary>
public static class SecureStorage
{
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("ApocalypseLauncher_v1_Salt_2026");

    /// <summary>
    /// Шифрование данных (работает на всех платформах)
    /// </summary>
    public static string Encrypt(string plainText)
    {
        try
        {
            // На Windows используем DPAPI
            if (OperatingSystem.IsWindows())
            {
                return EncryptWindows(plainText);
            }

            // На других платформах используем AES с машинным ключом
            return EncryptCrossPlatform(plainText);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SecureStorage] Encryption error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Расшифровка данных
    /// </summary>
    public static string Decrypt(string encryptedText)
    {
        try
        {
            // На Windows используем DPAPI
            if (OperatingSystem.IsWindows())
            {
                return DecryptWindows(encryptedText);
            }

            // На других платформах используем AES
            return DecryptCrossPlatform(encryptedText);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SecureStorage] Decryption error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Windows DPAPI шифрование
    /// </summary>
    private static string EncryptWindows(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = System.Security.Cryptography.ProtectedData.Protect(
            plainBytes,
            Salt,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        return "WIN:" + Convert.ToBase64String(protectedBytes);
    }

    /// <summary>
    /// Windows DPAPI расшифровка
    /// </summary>
    private static string DecryptWindows(string encryptedText)
    {
        if (encryptedText.StartsWith("WIN:"))
        {
            encryptedText = encryptedText.Substring(4);
        }

        var protectedBytes = Convert.FromBase64String(encryptedText);
        var plainBytes = System.Security.Cryptography.ProtectedData.Unprotect(
            protectedBytes,
            Salt,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// Кроссплатформенное AES шифрование
    /// </summary>
    private static string EncryptCrossPlatform(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Формат: "AES:" + IV (16 bytes) + encrypted data
        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

        return "AES:" + Convert.ToBase64String(result);
    }

    /// <summary>
    /// Кроссплатформенное AES расшифровка
    /// </summary>
    private static string DecryptCrossPlatform(string encryptedText)
    {
        if (encryptedText.StartsWith("AES:"))
        {
            encryptedText = encryptedText.Substring(4);
        }

        var data = Convert.FromBase64String(encryptedText);

        using var aes = Aes.Create();
        aes.Key = DeriveKey();

        // Извлекаем IV (первые 16 байт)
        var iv = new byte[16];
        Buffer.BlockCopy(data, 0, iv, 0, 16);
        aes.IV = iv;

        // Извлекаем зашифрованные данные
        var encryptedBytes = new byte[data.Length - 16];
        Buffer.BlockCopy(data, 16, encryptedBytes, 0, encryptedBytes.Length);

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// Генерация ключа на основе характеристик машины
    /// </summary>
    private static byte[] DeriveKey()
    {
        // Используем характеристики машины для генерации уникального ключа
        var machineId = GetMachineIdentifier();
        var keyMaterial = Encoding.UTF8.GetBytes(machineId + "_ApocalypseLauncher");

        // PBKDF2 для получения 256-битного ключа
        using var pbkdf2 = new Rfc2898DeriveBytes(keyMaterial, Salt, 10000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32); // 256 bits
    }

    /// <summary>
    /// Получение уникального идентификатора машины
    /// </summary>
    private static string GetMachineIdentifier()
    {
        try
        {
            // Комбинация имени машины и пользователя
            var machineName = Environment.MachineName;
            var userName = Environment.UserName;
            var osVersion = Environment.OSVersion.ToString();

            return $"{machineName}_{userName}_{osVersion}";
        }
        catch
        {
            // Fallback на случайный GUID (сохраняется в файле)
            var guidFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".apocalypse_launcher_id");

            if (File.Exists(guidFile))
            {
                return File.ReadAllText(guidFile);
            }

            var newGuid = Guid.NewGuid().ToString();
            Directory.CreateDirectory(Path.GetDirectoryName(guidFile)!);
            File.WriteAllText(guidFile, newGuid);
            return newGuid;
        }
    }
}
