using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ApocalypseLauncher.API.Controllers;

[ApiController]
[Route("api/modpack")]
[Authorize]
public class SimpleModpackController : ControllerBase
{
    private readonly ILogger<SimpleModpackController> _logger;
    private readonly HttpClient _httpClient;

    // НАСТРОЙКИ - ИЗМЕНИ ЭТО
    private const string GITHUB_REPO = "Bloody965/srp-rp-launcher";
    private const string MODPACK_RELEASE_TAG = "modpack-v1.0.0"; // Тег релиза с модпаком
    private const string MODPACK_FILENAME = "modpack.zip"; // Имя файла в релизе

    public SimpleModpackController(ILogger<SimpleModpackController> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SRP-RP-Launcher");
    }

    [HttpGet("version")]
    public async Task<ActionResult> GetLatestVersion()
    {
        try
        {
            // Получаем информацию о релизе с GitHub
            var url = $"https://api.github.com/repos/{GITHUB_REPO}/releases/tags/{MODPACK_RELEASE_TAG}";
            _logger.LogInformation($"Fetching modpack info from: {url}");

            var response = await _httpClient.GetStringAsync(url);
            var release = JsonDocument.Parse(response);

            // Ищем файл modpack.zip в assets
            var assets = release.RootElement.GetProperty("assets");
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name == MODPACK_FILENAME)
                {
                    var downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    var sizeBytes = asset.GetProperty("size").GetInt64();

                    _logger.LogInformation($"Found modpack: {downloadUrl}, Size: {sizeBytes} bytes");

                    return Ok(new
                    {
                        version = MODPACK_RELEASE_TAG.Replace("modpack-v", ""),
                        downloadUrl = downloadUrl,
                        sha256Hash = "skip", // Лаунчер пропустит проверку если "skip"
                        fileSizeBytes = sizeBytes,
                        changelog = "Сборка модов для SRP-RP сервера"
                    });
                }
            }

            _logger.LogWarning($"Modpack file '{MODPACK_FILENAME}' not found in release");
            return NotFound(new { message = $"Файл {MODPACK_FILENAME} не найден в релизе {MODPACK_RELEASE_TAG}" });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError($"GitHub API error: {ex.Message}");
            return StatusCode(500, new { message = "Ошибка получения информации о сборке с GitHub" });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error: {ex.Message}");
            return StatusCode(500, new { message = "Ошибка сервера" });
        }
    }
}
