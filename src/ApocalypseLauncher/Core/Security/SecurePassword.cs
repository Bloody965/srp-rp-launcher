using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace ApocalypseLauncher.Core.Security;

/// <summary>
/// Безопасное хранение паролей в памяти
/// Альтернатива SecureString (который deprecated)
/// </summary>
public sealed class SecurePassword : IDisposable
{
    private byte[]? _encryptedData;
    private readonly byte[] _entropy;
    private bool _disposed;

    public SecurePassword()
    {
        _entropy = new byte[16];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(_entropy);
    }

    /// <summary>
    /// Установить пароль
    /// </summary>
    public void SetPassword(string password)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SecurePassword));

        if (string.IsNullOrEmpty(password))
        {
            Clear();
            return;
        }

        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(password);

            // Шифруем в памяти
            if (OperatingSystem.IsWindows())
            {
                _encryptedData = System.Security.Cryptography.ProtectedData.Protect(
                    plainBytes,
                    _entropy,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
            }
            else
            {
                // На других платформах используем XOR с энтропией (базовая защита)
                _encryptedData = new byte[plainBytes.Length];
                for (int i = 0; i < plainBytes.Length; i++)
                {
                    _encryptedData[i] = (byte)(plainBytes[i] ^ _entropy[i % _entropy.Length]);
                }
            }

            // Очищаем исходный массив из памяти
            Array.Clear(plainBytes, 0, plainBytes.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SecurePassword] Error setting password: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Получить пароль (временно расшифровывается)
    /// </summary>
    public string GetPassword()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SecurePassword));

        if (_encryptedData == null || _encryptedData.Length == 0)
            return string.Empty;

        byte[]? plainBytes = null;
        try
        {
            // Расшифровываем
            if (OperatingSystem.IsWindows())
            {
                plainBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                    _encryptedData,
                    _entropy,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
            }
            else
            {
                // XOR расшифровка
                plainBytes = new byte[_encryptedData.Length];
                for (int i = 0; i < _encryptedData.Length; i++)
                {
                    plainBytes[i] = (byte)(_encryptedData[i] ^ _entropy[i % _entropy.Length]);
                }
            }

            return Encoding.UTF8.GetString(plainBytes);
        }
        finally
        {
            // Очищаем расшифрованные данные из памяти
            if (plainBytes != null)
            {
                Array.Clear(plainBytes, 0, plainBytes.Length);
            }
        }
    }

    /// <summary>
    /// Проверка что пароль установлен
    /// </summary>
    public bool IsEmpty => _encryptedData == null || _encryptedData.Length == 0;

    /// <summary>
    /// Очистить пароль из памяти
    /// </summary>
    public void Clear()
    {
        if (_encryptedData != null)
        {
            Array.Clear(_encryptedData, 0, _encryptedData.Length);
            _encryptedData = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Clear();
        Array.Clear(_entropy, 0, _entropy.Length);
        _disposed = true;
    }
}
