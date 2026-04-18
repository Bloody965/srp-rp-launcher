using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ApocalypseLauncher.Core.Security;

/// <summary>
/// Certificate pinning для защиты от MITM атак
/// </summary>
public class CertificatePinning
{
    // SHA256 хеши публичных ключей доверенных сертификатов
    private static readonly HashSet<string> TrustedCertificateHashes = new()
    {
        // Railway production certificate
        "VYxe9LAwK2QozwAdcQXon+QWur/Wn6o01PdWoMq1jiw=",

        // Development localhost (для тестирования)
        "DEVELOPMENT_CERTIFICATE_HASH_PLACEHOLDER"
    };

    private readonly bool _isDevelopment;

    public CertificatePinning(bool isDevelopment = false)
    {
        _isDevelopment = isDevelopment;
    }

    /// <summary>
    /// Валидация SSL сертификата
    /// </summary>
    public bool ValidateCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        // В режиме разработки разрешаем self-signed сертификаты
        if (_isDevelopment)
        {
            Console.WriteLine("[CertificatePinning] Development mode - accepting certificate");
            return true;
        }

        // Проверка базовых ошибок SSL
        if (sslPolicyErrors != SslPolicyErrors.None)
        {
            Console.WriteLine($"[CertificatePinning] SSL Policy Error: {sslPolicyErrors}");
            return false;
        }

        if (certificate == null)
        {
            Console.WriteLine("[CertificatePinning] Certificate is null");
            return false;
        }

        // Certificate pinning - проверка хеша публичного ключа
        try
        {
            var cert2 = new X509Certificate2(certificate);
            var publicKey = cert2.GetPublicKey();
            var hash = SHA256.HashData(publicKey);
            var hashString = Convert.ToBase64String(hash);

            Console.WriteLine($"[CertificatePinning] Certificate hash: {hashString}");
            Console.WriteLine($"[CertificatePinning] Subject: {cert2.Subject}");
            Console.WriteLine($"[CertificatePinning] Issuer: {cert2.Issuer}");
            Console.WriteLine($"[CertificatePinning] Valid from: {cert2.NotBefore} to {cert2.NotAfter}");

            if (TrustedCertificateHashes.Contains(hashString))
            {
                Console.WriteLine("[CertificatePinning] Certificate pinning: PASSED");
                return true;
            }

            Console.WriteLine("[CertificatePinning] Certificate pinning: FAILED - Unknown certificate");
            Console.WriteLine($"[CertificatePinning] Add this hash to TrustedCertificateHashes: {hashString}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CertificatePinning] Error validating certificate: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Создает HttpClientHandler с certificate pinning
    /// </summary>
    public HttpClientHandler CreateSecureHandler()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = ValidateCertificate
        };

        return handler;
    }

    /// <summary>
    /// Генерирует хеш сертификата для добавления в whitelist
    /// </summary>
    public static string GetCertificateHash(X509Certificate2 certificate)
    {
        var publicKey = certificate.GetPublicKey();
        var hash = SHA256.HashData(publicKey);
        return Convert.ToBase64String(hash);
    }
}
