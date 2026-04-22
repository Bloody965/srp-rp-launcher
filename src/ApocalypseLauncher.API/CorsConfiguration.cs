using System.Collections.Generic;

namespace ApocalypseLauncher.API;

/// <summary>Сборка списка origin для CORS: Cors:AllowedOrigins[], Site:PublicOrigin, SITE_PUBLIC_ORIGIN / SITE_ORIGIN.</summary>
internal static class CorsConfiguration
{
    /// <summary>Та же логика, что и в политике CORS — для ручного preflight и SetIsOriginAllowed.</summary>
    public static bool IsOriginAllowed(
        string? origin,
        string[] allowedOrigins,
        bool corsAllowAny,
        bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return false;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;

        if (corsAllowAny)
            return true;

        var normalized = $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}";
        foreach (var o in allowedOrigins)
        {
            if (string.Equals(normalized, o, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (isDevelopment)
            return true;

        if (allowedOrigins.Length == 0)
            return true;

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            var h = uri.IdnHost;
            if (h.EndsWith(".workers.dev", StringComparison.OrdinalIgnoreCase))
                return true;
            if (h.EndsWith(".pages.dev", StringComparison.OrdinalIgnoreCase))
                return true;
            if (h.Equals("github.io", StringComparison.OrdinalIgnoreCase)
                || h.EndsWith(".github.io", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (uri.Scheme == Uri.UriSchemeHttp
            && (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    public static string[] GetAllowedOrigins(IConfiguration configuration)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRaw(string? raw)
        {
            var t = raw?.Trim();
            if (string.IsNullOrWhiteSpace(t))
                return;
            if (!Uri.TryCreate(t, UriKind.Absolute, out var u))
                return;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)
                return;
            var port = u.IsDefaultPort ? "" : $":{u.Port}";
            set.Add($"{u.Scheme}://{u.Host}{port}");
        }

        foreach (var child in configuration.GetSection("Cors:AllowedOrigins").GetChildren())
            AddRaw(child.Value);

        AddRaw(configuration["Site:PublicOrigin"]);
        AddRaw(Environment.GetEnvironmentVariable("SITE_PUBLIC_ORIGIN"));
        AddRaw(Environment.GetEnvironmentVariable("SITE_ORIGIN"));

        return set.Count > 0 ? set.ToArray() : Array.Empty<string>();
    }
}
