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
    private readonly RateLimitService _rateLimitService;

    public LogAnalyzerController(
        ILogger<LogAnalyzerController> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        RateLimitService rateLimitService)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient();
        _rateLimitService = rateLimitService;
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> AnalyzeLogs([FromBody] LogAnalysisRequest request)
    {
        try
        {
            // Rate limiting: 5 запросов в минуту на IP
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!_rateLimitService.IsAllowed(clientIp, "log_analysis", 5, TimeSpan.FromMinutes(1)))
            {
                return StatusCode(429, new { success = false, message = "Слишком много запросов. Подождите минуту." });
            }

            if (string.IsNullOrWhiteSpace(request.Logs))
            {
                return BadRequest(new { success = false, message = "Логи не предоставлены" });
            }

            // Берем последние 10000 символов для анализа
            var logsToAnalyze = request.Logs.Length > 10000
                ? request.Logs.Substring(request.Logs.Length - 10000)
                : request.Logs;

            var apiKey = _configuration["Groq:ApiKey"]
                ?? Environment.GetEnvironmentVariable("GROQ_API_KEY");

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
                model = "llama-3.1-70b-versatile",
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
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var response = await _httpClient.PostAsync(
                "https://api.groq.com/openai/v1/chat/completions",
                new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"
                )
            );

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Groq API error: {response.StatusCode} - {error}");

                return Ok(new
                {
                    success = true,
                    analysis = "Не удалось проанализировать логи через AI. Проверьте логи вручную или обратитесь в Discord поддержку."
                });
            }

            var result = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(result);
            var content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

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
