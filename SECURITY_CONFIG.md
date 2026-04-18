# Security Configuration

## Certificate Pinning

To enable certificate pinning for your production server:

1. Get your server's certificate hash:
```bash
openssl s_client -connect your-domain.com:443 | openssl x509 -pubkey -noout | openssl pkey -pubin -outform der | openssl dgst -sha256 -binary | openssl enc -base64
```

2. Add the hash to `CertificatePinning.cs`:
```csharp
private static readonly HashSet<string> TrustedCertificateHashes = new()
{
    "YOUR_CERTIFICATE_HASH_HERE"
};
```

3. Update API base URL in your launcher configuration to use HTTPS.

## Anti-Debugging

The launcher includes anti-debugging protection that:
- Detects managed and native debuggers
- Checks for remote debuggers
- Uses timing attacks to detect debugging
- Terminates if debugger is detected

**Note:** This protection is automatically disabled in DEBUG builds.

## Secure Storage

Tokens are encrypted using:
- **Windows:** DPAPI (Data Protection API)
- **Linux/Mac:** AES-256 with machine-specific key

## Production Checklist

- [ ] Replace certificate hash in `CertificatePinning.cs`
- [ ] Set API base URL to HTTPS production server
- [ ] Build in RELEASE mode
- [ ] Test certificate pinning with production server
- [ ] Verify anti-debugging works (try attaching debugger)
- [ ] Test on all target platforms (Windows/Linux/Mac)

## Security Features Summary

✅ HTTPS enforcement (except localhost)
✅ Certificate pinning (MITM protection)
✅ Anti-debugging (reverse engineering protection)
✅ Integrity checks (tampering detection)
✅ Secure token storage (cross-platform encryption)
✅ No password logging
✅ Memory protection for sensitive data

## Disabling Security (Development Only)

To disable security features for development:

1. Use `http://localhost:5000` as API URL (certificate pinning auto-disabled)
2. Build in DEBUG mode (anti-debugging auto-disabled)
3. Never deploy with security disabled!
