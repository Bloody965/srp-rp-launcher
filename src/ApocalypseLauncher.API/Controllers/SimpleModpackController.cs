using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApocalypseLauncher.API.Controllers;

[ApiController]
[Route("api/modpack")]
[Authorize]
public class SimpleModpackController : ControllerBase
{
    private readonly ILogger<SimpleModpackController> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    private const string DefaultGithubRepo = "Bloody965/srp-rp-launcher";
    private const string DefaultModpackReleaseTag = "modpack-v1.0.0";
    private const string DefaultModpackFilename = "modpack.zip";

    public SimpleModpackController(ILogger<SimpleModpackController> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SRP-RP-Launcher");
    }

    [HttpGet("version")]
    public async Task<ActionResult> GetLatestVersion()
    {
        try
        {
            var sha256Hash = _configuration["Modpack:Sha256Hash"];
            if (string.IsNullOrWhiteSpace(sha256Hash))
            {
                _logger.LogError("Modpack SHA256 hash is not configured.");
                return StatusCode(500, new { message = "SHA256 hash for modpack is not configured" });
            }

            // Preferred mode: serve version and URL from your infrastructure config.
            var configuredVersion = _configuration["Modpack:Version"];
            var configuredDownloadUrl = _configuration["Modpack:DownloadUrl"];
            var configuredFileSize = _configuration.GetValue<long?>("Modpack:FileSizeBytes");
            var configuredChangelog = _configuration["Modpack:Changelog"];

            if (!string.IsNullOrWhiteSpace(configuredVersion) &&
                !string.IsNullOrWhiteSpace(configuredDownloadUrl))
            {
                _logger.LogInformation("Using configured Modpack URL from Modpack:DownloadUrl");
                return Ok(new
                {
                    version = configuredVersion.Trim(),
                    downloadUrl = configuredDownloadUrl.Trim(),
                    sha256Hash,
                    fileSizeBytes = configuredFileSize ?? 0L,
                    changelog = string.IsNullOrWhiteSpace(configuredChangelog)
                        ? "Сборка модов для SRP-RP сервера"
                        : configuredChangelog.Trim()
                });
            }

            var githubRepo = _configuration["Modpack:GithubRepo"] ?? DefaultGithubRepo;
            var releaseTag = _configuration["Modpack:GithubReleaseTag"] ?? DefaultModpackReleaseTag;
            var filename = _configuration["Modpack:GithubAssetName"] ?? DefaultModpackFilename;

            var url = $"https://api.github.com/repos/{githubRepo}/releases/tags/{releaseTag}";
            _logger.LogInformation("Fetching modpack info from GitHub release tag {Tag}", releaseTag);

            var response = await _httpClient.GetStringAsync(url);
            using var release = JsonDocument.Parse(response);

            var assets = release.RootElement.GetProperty("assets");
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name == filename)
                {
                    var downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    var sizeBytes = asset.GetProperty("size").GetInt64();

                    return Ok(new
                    {
                        version = releaseTag.Replace("modpack-v", ""),
                        downloadUrl,
                        sha256Hash,
                        fileSizeBytes = sizeBytes,
                        changelog = "Сборка модов для SRP-RP сервера"
                    });
                }
            }

            _logger.LogWarning("Modpack asset {Filename} not found in release {Tag}", filename, releaseTag);
            return NotFound(new { message = $"Файл {filename} не найден в релизе {releaseTag}" });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GitHub API error while fetching modpack release");
            return StatusCode(500, new { message = "Ошибка получения информации о сборке с GitHub" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error while getting modpack version");
            return StatusCode(500, new { message = "Ошибка сервера" });
        }
    }
}
