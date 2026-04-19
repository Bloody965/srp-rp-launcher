using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace ApocalypseLauncher.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogAnalyzerController : ControllerBase
{
    private readonly ILogger<LogAnalyzerController> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public LogAnalyzerController(
        ILogger<LogAnalyzerController> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient();
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> AnalyzeLogs([FromBody] LogAnalysisRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Logs))
            {
                return BadRequest(new { success = false, message = "Логи не предоставлены" });
            }

            // Берем последние 10000 символов для анализа
            var logsToAnalyze = request.Logs.Length > 10000
                ? request.Logs.Substring(request.Logs.Length - 10000)
                : request.Logs;

            var apiKey = _configuration["Anthropic:ApiKey"]
                ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return Ok(new
                {
                    success = true,
                    analysis = "AI анализ временно недоступен. Проверьте логи вручную или обратитесь в Discord поддержку."
                });
            }

            var requestBody = new
            {
                model = "claude-3-5-sonnet-20241022",
                max_tokens = 1024,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = $@"Проанализируй логи Minecraft Forge 1.20.1 и определи:
1. Есть ли ошибки или краши?
2. В чем причина проблемы?
3. Как это исправить?

Ответь на русском языке, кратко и понятно для обычного игрока.
Если ошибок нет - скажи что все работает нормально.

ЛОГИ:
{logsToAnalyze}"
                    }
                }
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var response = await _httpClient.PostAsync(
                "https://api.anthropic.com/v1/messages",
                new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"
                )
            );

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Anthropic API error: {response.StatusCode} - {error}");

                return Ok(new
                {
                    success = true,
                    analysis = "Не удалось проанализировать логи через AI. Проверьте логи вручную или обратитесь в Discord поддержку."
                });
            }

            var result = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(result);
            var content = json.RootElement.GetProperty("content")[0].GetProperty("text").GetString();

            return Ok(new
            {
                success = true,
                analysis = content ?? "AI не смог проанализировать логи"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error analyzing logs: {ex.Message}");
            return Ok(new
            {
                success = true,
                analysis = $"Ошибка анализа: {ex.Message}\n\nПопробуйте позже или обратитесь в поддержку Discord."
            });
        }
    }
}

public class LogAnalysisRequest
{
    public string Logs { get; set; } = string.Empty;
}
