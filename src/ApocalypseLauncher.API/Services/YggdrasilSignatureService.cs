using System.Security.Cryptography;
using System.Text;

namespace ApocalypseLauncher.API.Services;

public class YggdrasilSignatureService : IDisposable
{
    private readonly ILogger<YggdrasilSignatureService> _logger;
    private readonly RSA _rsa;

    public string PublicKeyBase64 { get; }

    public YggdrasilSignatureService(IConfiguration configuration, ILogger<YggdrasilSignatureService> logger)
    {
        _logger = logger;
        _rsa = RSA.Create();

        // Optional persistent key from config/env:
        // Yggdrasil:PrivateKeyPkcs8Base64 -> base64 of PKCS#8 private key bytes.
        var privateKeyBase64 = configuration["Yggdrasil:PrivateKeyPkcs8Base64"]
            ?? Environment.GetEnvironmentVariable("YGGDRASIL_PRIVATE_KEY_PKCS8_BASE64");

        if (!string.IsNullOrWhiteSpace(privateKeyBase64))
        {
            try
            {
                var privateKeyBytes = Convert.FromBase64String(privateKeyBase64.Trim());
                _rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
                _logger.LogInformation("Loaded Yggdrasil signing key from configuration.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to import configured Yggdrasil private key, using ephemeral key. {Message}", ex.Message);
                _rsa.KeySize = 2048;
            }
        }
        else
        {
            // Fallback: ephemeral runtime key. Works while server is running, but clients may need refresh after restart.
            _rsa.KeySize = 2048;
            _logger.LogWarning("Yggdrasil private key not configured, using ephemeral runtime key.");
        }

        PublicKeyBase64 = Convert.ToBase64String(_rsa.ExportSubjectPublicKeyInfo());
    }

    public string SignTextures(string texturesBase64)
    {
        var data = Encoding.UTF8.GetBytes(texturesBase64);
        var signature = _rsa.SignData(data, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }
}

