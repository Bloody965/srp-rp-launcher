using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApocalypseLauncher.API.Controllers;

[ApiController]
[Route("api/launcher")]
[Authorize]
public class LauncherUpdatesController : ControllerBase
{
    private readonly ILogger<LauncherUpdatesController> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;

    private const string DefaultGithubRepo = "Bloody965/srp-rp-launcher";
    private const string DefaultLauncherAssetName = "ApocalypseLauncher.zip";

    public LauncherUpdatesController(ILogger<LauncherUpdatesController> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SRP-RP-Launcher");
    }

    [HttpGet("version")]
    public async Task<ActionResult> GetLatestLauncherVersion()
    {
        try
        {
            // Preferred mode: your own CDN/storage URL via config.
            var configuredVersion = _configuration["LauncherUpdate:Version"];
            var configuredDownloadUrl = _configuration["LauncherUpdate:DownloadUrl"];
            var configuredFileSize = _configuration.GetValue<long?>("LauncherUpdate:FileSizeBytes");
            var configuredSha256 = _configuration["LauncherUpdate:Sha256Hash"];
            var configuredChangelog = _configuration["LauncherUpdate:Changelog"];

            if (!string.IsNullOrWhiteSpace(configuredVersion) &&
                !string.IsNullOrWhiteSpace(configuredDownloadUrl))
            {
                return Ok(new
                {
                    version = configuredVersion.Trim(),
                    downloadUrl = configuredDownloadUrl.Trim(),
                    fileSizeBytes = configuredFileSize ?? 0L,
                    sha256Hash = configuredSha256 ?? string.Empty,
                    changelog = configuredChangelog ?? "Обновление лаунчера"
                });
            }

            // Fallback mode: GitHub latest release (legacy behavior).
            var githubRepo = _configuration["LauncherUpdate:GithubRepo"] ?? DefaultGithubRepo;
            var launcherAssetName = _configuration["LauncherUpdate:GithubAssetName"] ?? DefaultLauncherAssetName;
            var url = $"https://api.github.com/repos/{githubRepo}/releases/latest";
            var response = await _httpClient.GetStringAsync(url);
            using var release = JsonDocument.Parse(response);

            var tagName = release.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
            var latestVersion = tagName.TrimStart('v');
            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                return NotFound(new { message = "Не удалось определить версию лаунчера" });
            }

            string? downloadUrl = null;
            long fileSizeBytes = 0;
            if (release.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    if (name == launcherAssetName)
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        fileSizeBytes = asset.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return NotFound(new { message = $"Файл {launcherAssetName} не найден в latest release" });
            }

            return Ok(new
            {
                version = latestVersion,
                downloadUrl,
                fileSizeBytes,
                sha256Hash = string.Empty,
                changelog = "Обновление лаунчера"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve launcher update metadata");
            return StatusCode(500, new { message = "Ошибка получения информации об обновлении лаунчера" });
        }
    }
}
