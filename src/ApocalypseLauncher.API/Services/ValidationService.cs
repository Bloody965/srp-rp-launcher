using System.Text.RegularExpressions;

namespace ApocalypseLauncher.API.Services;

public class ValidationService
{
    // Защита от SQL инъекций и XSS
    public static bool IsValidUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return false;

        // Только буквы, цифры и подчеркивание, 3-16 символов
        return Regex.IsMatch(username, @"^[a-zA-Z0-9_]{3,16}$");
    }

    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        // Базовая проверка email
        return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$") && email.Length <= 100;
    }

    public static bool IsValidVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        // Только цифры и точки, например 1.0.0
        return Regex.IsMatch(version, @"^\d+\.\d+\.\d+$") && version.Length <= 20;
    }

    public static string SanitizeInput(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // Удаляем потенциально опасные символы
        var dangerous = new[] { "<", ">", "'", "\"", ";", "--", "/*", "*/", "xp_", "sp_", "exec", "execute", "select", "insert", "update", "delete", "drop", "create", "alter", "union" };

        var sanitized = input;
        foreach (var danger in dangerous)
        {
            sanitized = sanitized.Replace(danger, "", StringComparison.OrdinalIgnoreCase);
        }

        return sanitized.Trim();
    }

    public static bool ContainsSqlInjection(string input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        var sqlPatterns = new[]
        {
            @"(\b(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|EXEC|EXECUTE|UNION|DECLARE)\b)",
            @"(--|;|\/\*|\*\/)",
            @"(\bOR\b.*=.*)",
            @"(\bAND\b.*=.*)",
            @"('.*--)",
            @"(xp_|sp_)"
        };

        foreach (var pattern in sqlPatterns)
        {
            if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }
}
