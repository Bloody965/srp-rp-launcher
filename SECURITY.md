# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | :white_check_mark: |

## Reporting a Vulnerability

If you discover a security vulnerability, please do NOT open a public issue.

Instead:

1. **Create a private security advisory** on GitHub
2. Or email the maintainers directly
3. Include:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if any)

We will respond within 48 hours and work on a fix as soon as possible.

## Security Best Practices

### For Server Administrators

1. **Never commit secrets** to git
   - Use environment variables for production
   - Keep `appsettings.json` out of version control if it contains secrets

2. **Use strong JWT keys**
   - Minimum 64 characters
   - Cryptographically random
   - Rotate periodically

3. **Enable HTTPS** in production
   - Use valid SSL/TLS certificates
   - Disable HTTP in production

4. **Configure CORS properly**
   - Don't use `AllowAnyOrigin` in production
   - Specify exact allowed domains

5. **Monitor logs** for suspicious activity
   - Failed login attempts
   - SQL injection attempts
   - Rate limit violations

6. **Keep dependencies updated**
   ```bash
   dotnet list package --outdated
   dotnet add package [PackageName] --version [LatestVersion]
   ```

7. **Database backups**
   - Regular automated backups
   - Test restore procedures

### For Users

1. **Use strong passwords**
   - Minimum 8 characters
   - Mix of uppercase, lowercase, and numbers
   - Don't reuse passwords

2. **Save your recovery code**
   - Shown only once during registration
   - Store it securely
   - Needed for password reset

3. **Don't share your account**
   - Each player should have their own account
   - Don't share JWT tokens

## Known Security Considerations

### Rate Limiting
- Registration: 3 attempts per hour per IP
- Login: 5 attempts per 15 minutes per IP
- Password reset: 5 attempts per 15 minutes per IP

### Password Requirements
- Minimum 8 characters
- At least one uppercase letter
- At least one lowercase letter
- At least one digit

### JWT Tokens
- 24-hour expiration (configurable)
- Stored as SHA256 hash in database
- Revoked on password change

### SQL Injection Protection
- Parameterized queries via Entity Framework
- Input validation on all endpoints
- Regex-based SQL pattern detection

### XSS Protection
- Input sanitization
- Username restricted to alphanumeric + underscore
- No HTML allowed in user inputs

## Security Audit History

- **2026-04-18**: Initial security review
  - Removed hardcoded server IP from repository
  - Added appsettings.example.json template
  - Updated .gitignore for sensitive files
  - Created security documentation

## Compliance

This project does NOT collect or store:
- Email addresses (removed for GDPR compliance)
- Payment information
- Personal identification documents

Stored data:
- Username (public identifier)
- Password hash (BCrypt, 12 rounds)
- Recovery code hash (BCrypt, 12 rounds)
- Minecraft UUID (generated from username)
- IP addresses in audit logs (for security)
- Login timestamps
- Playtime statistics

## Third-Party Dependencies

Regular security audits of dependencies:
```bash
dotnet list package --vulnerable
```

Critical dependencies:
- BCrypt.Net-Next (password hashing)
- Microsoft.AspNetCore.Authentication.JwtBearer (JWT validation)
- Microsoft.EntityFrameworkCore (database ORM)

## Contact

For security concerns: Create a private security advisory on GitHub
