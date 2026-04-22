using System;

namespace ApocalypseLauncher.Core;

/// <summary>
/// Публичный URL API SRP-RP. Должен совпадать с <c>data-auth-api</c> на сайте и с <c>BaseUrl</c> / доменом Railway.
/// Переопределение без пересборки: переменная окружения <c>SRP_API_BASE_URL</c> (например <c>https://api.example.com</c>).
/// </summary>
public static class SrpProjectEndpoints
{
    public const string DefaultApiBaseUrl = "https://srp-rp-launcher-production.up.railway.app";

    private static readonly string ResolvedApiBase = ResolveApiBase();

    public static string ApiBaseUrl => ResolvedApiBase;

    public static string YggdrasilRootUrl => $"{ResolvedApiBase.TrimEnd('/')}/api/yggdrasil";

    public static Uri LogAnalyzerAnalyzeUri => new($"{ResolvedApiBase.TrimEnd('/')}/api/LogAnalyzer/analyze");

    private static string ResolveApiBase()
    {
        var env = Environment.GetEnvironmentVariable("SRP_API_BASE_URL")?.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(env))
            return env;
        return DefaultApiBaseUrl;
    }
}
