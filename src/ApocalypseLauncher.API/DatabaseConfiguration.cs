using System;

namespace ApocalypseLauncher.API;

/// <summary>Выбор PostgreSQL vs SQLite и нормализация строки подключения (Railway, Amvera, Npgsql).</summary>
internal static class DatabaseConfiguration
{
    public static bool IsSqliteConnectionString(string? connectionString) =>
        !string.IsNullOrWhiteSpace(connectionString) &&
        connectionString.TrimStart().StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase);

    public static bool LooksLikePostgreSqlConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var s = connectionString.TrimStart();
        if (s.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase) &&
               connectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Превращает postgres:// URI в ключ=значение; для Amvera/облака добавляет SSL, если не указано.</summary>
    public static string? NormalizePostgreSqlConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var s = connectionString.Trim();
        if (s.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(s);
                var userInfo = uri.UserInfo;
                string user;
                string password;

                var colon = userInfo.IndexOf(':');
                if (colon >= 0)
                {
                    user = Uri.UnescapeDataString(userInfo[..colon]);
                    password = Uri.UnescapeDataString(userInfo[(colon + 1)..]);
                }
                else
                {
                    user = Uri.UnescapeDataString(userInfo);
                    password = string.Empty;
                }

                var database = uri.AbsolutePath.TrimStart('/');
                var port = uri.Port > 0 ? uri.Port : 5432;
                return $"Host={uri.Host};Port={port};Database={database};Username={user};Password={password};SSL Mode=Prefer;Trust Server Certificate=true";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Startup] Error converting PostgreSQL URI: {ex.Message}");
                return null;
            }
        }

        if (!s.Contains("SSL Mode", StringComparison.OrdinalIgnoreCase) &&
            !s.Contains("Ssl Mode", StringComparison.OrdinalIgnoreCase))
        {
            s = s.TrimEnd().TrimEnd(';') + ";SSL Mode=Prefer";
        }

        if (!s.Contains("Trust Server Certificate", StringComparison.OrdinalIgnoreCase) &&
            !s.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase))
        {
            s = s.TrimEnd().TrimEnd(';') + ";Trust Server Certificate=true";
        }

        return s;
    }
}
