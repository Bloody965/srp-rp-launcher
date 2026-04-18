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

    private const string GITHUB_REPO = "Bloody965/srp-rp-launcher";
    private const string MODPACK_RELEASE_TAG = "modpack-v1.0.0";
    private const string MODPACK_FILENAME = "modpack.zip";

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

            var url = $"https://api.github.com/repos/{GITHUB_REPO}/releases/tags/{MODPACK_RELEASE_TAG}";
            _logger.LogInformation("Fetching modpack info from GitHub release tag {Tag}", MODPACK_RELEASE_TAG);

            var response = await _httpClient.GetStringAsync(url);
            using var release = JsonDocument.Parse(response);

            var assets = release.RootElement.GetProperty("assets");
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name == MODPACK_FILENAME)
                {
                    var downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    var sizeBytes = asset.GetProperty("size").GetInt64();

                    return Ok(new
                    {
                        version = MODPACK_RELEASE_TAG.Replace("modpack-v", ""),
                        downloadUrl,
                        sha256Hash,
                        fileSizeBytes = sizeBytes,
                        changelog = "Сборка модов для SRP-RP сервера"
                    });
                }
            }

            _logger.LogWarning("Modpack asset {Filename} not found in release {Tag}", MODPACK_FILENAME, MODPACK_RELEASE_TAG);
            return NotFound(new { message = $"Файл {MODPACK_FILENAME} не найден в релизе {MODPACK_RELEASE_TAG}" });
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
