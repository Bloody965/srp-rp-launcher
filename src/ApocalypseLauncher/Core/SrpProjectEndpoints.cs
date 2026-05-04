using System;
using System.IO;

namespace ApocalypseLauncher.Core;

/// <summary>
/// Публичный URL API. Должен совпадать с <c>BaseUrl</c> на сервере (скины/Yggdrasil иначе отваливаются).
/// Приоритет: <c>SRP_API_BASE_URL</c> → файл <c>%AppData%\SRP-RP-Launcher\api-base.url</c> (одна строка с https) → встроенный дефолт.
/// </summary>
public static class SrpProjectEndpoints
{
    /// <summary>Актуальный прод-хост; старый Railway можно вернуть через env или api-base.url.</summary>
    public const string DefaultApiBaseUrl = "https://srp-132412454523.amvera.io";

    private static readonly string ResolvedApiBase = ResolveApiBase();

    public static string ApiBaseUrl => ResolvedApiBase;

    public static string YggdrasilRootUrl => $"{ResolvedApiBase.TrimEnd('/')}/api/yggdrasil";

    public static Uri LogAnalyzerAnalyzeUri => new($"{ResolvedApiBase.TrimEnd('/')}/api/LogAnalyzer/analyze");

    private static string ResolveApiBase()
    {
        var env = Environment.GetEnvironmentVariable("SRP_API_BASE_URL")?.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        var fromFile = TryReadApiBaseFromUserFile();
        if (!string.IsNullOrWhiteSpace(fromFile))
            return fromFile;

        return DefaultApiBaseUrl;
    }

    private static string? TryReadApiBaseFromUserFile()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var path = Path.Combine(appData, "SRP-RP-Launcher", "api-base.url");
            if (!File.Exists(path))
            {
                return null;
            }

            var line = File.ReadAllText(path).Trim();
            foreach (var raw in line.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = raw.Trim().TrimEnd('/');
                if (candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[SrpProjectEndpoints] Using API base from api-base.url");
                    return candidate;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SrpProjectEndpoints] api-base.url read failed: {ex.Message}");
        }

        return null;
    }
}
