using System.Collections.Generic;

namespace ApocalypseLauncher.API;

/// <summary>Сборка списка origin для CORS: Cors:AllowedOrigins[], Site:PublicOrigin, SITE_PUBLIC_ORIGIN / SITE_ORIGIN.</summary>
internal static class CorsConfiguration
{
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
