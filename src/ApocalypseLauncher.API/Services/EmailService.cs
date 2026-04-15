using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ApocalypseLauncher.API.Services;

public class EmailService
{
    private readonly string _apiKey;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly HttpClient _httpClient;

    public EmailService(IConfiguration configuration)
    {
        _apiKey = Environment.GetEnvironmentVariable("RESEND_API_KEY")
                  ?? configuration["Resend:ApiKey"]
                  ?? throw new Exception("Resend API key not configured");

        _fromEmail = Environment.GetEnvironmentVariable("RESEND_FROM_EMAIL")
                     ?? configuration["Resend:FromEmail"]
                     ?? throw new Exception("Resend FromEmail not configured");

        _fromName = Environment.GetEnvironmentVariable("RESEND_FROM_NAME")
                    ?? configuration["Resend:FromName"]
                    ?? "SRP-RP Launcher";

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string username, string resetCode)
    {
        try
        {
            Console.WriteLine($"[EmailService] Начало отправки email на: {toEmail}");
            Console.WriteLine($"[EmailService] API Key: {(_apiKey?.Length > 10 ? _apiKey.Substring(0, 10) + "..." : "NOT SET")}");
            Console.WriteLine($"[EmailService] From Email: {_fromEmail}");

            var htmlContent = $@"
                <html>
                <body style='font-family: Arial, sans-serif; background-color: #0d1420; color: #edf3fb; padding: 20px;'>
                    <div style='max-width: 600px; margin: 0 auto; background-color: #0c121b; border: 1px solid #ff9264; border-radius: 10px; padding: 30px;'>
                        <h1 style='color: #ff4d5d; text-align: center;'>SRP-RP LAUNCHER</h1>
                        <h2 style='color: #ff9264;'>Сброс пароля</h2>
                        <p>Привет, <strong>{username}</strong>!</p>
                        <p>Вы запросили сброс пароля для вашего аккаунта.</p>
                        <p>Ваш код для сброса пароля:</p>
                        <div style='background-color: #1a0d0d; border: 2px solid #ff4d5d; border-radius: 5px; padding: 20px; text-align: center; margin: 20px 0;'>
                            <h1 style='color: #ff4d5d; font-size: 32px; letter-spacing: 5px; margin: 0;'>{resetCode}</h1>
                        </div>
                        <p>Введите этот код в лаунчере для сброса пароля.</p>
                        <p style='color: #9fb0c3; font-size: 12px;'>Код действителен в течение 15 минут.</p>
                        <hr style='border: 1px solid #ff9264; margin: 30px 0;'>
                        <p style='color: #9fb0c3; font-size: 11px; text-align: center;'>
                            Если вы не запрашивали сброс пароля, просто проигнорируйте это письмо.
                        </p>
                        <p style='color: #6cc8ff; font-size: 10px; text-align: center;'>
                            POST-APOCALYPSE RESIDENT RP | 2026
                        </p>
                    </div>
                </body>
                </html>
            ";

            var plainTextContent = $@"SRP-RP LAUNCHER - Сброс пароля

Привет, {username}!

Вы запросили сброс пароля для вашего аккаунта.

Ваш код для сброса пароля: {resetCode}

Введите этот код в лаунчере для сброса пароля.
Код действителен в течение 15 минут.

Если вы не запрашивали сброс пароля, просто проигнорируйте это письмо.

POST-APOCALYPSE RESIDENT RP | 2026";

            Console.WriteLine($"[EmailService] Создание письма...");

            var payload = new
            {
                from = $"{_fromName} <{_fromEmail}>",
                to = new[] { toEmail },
                subject = "Сброс пароля - SRP-RP Launcher",
                html = htmlContent,
                text = plainTextContent
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            Console.WriteLine($"[EmailService] Отправка через Resend API...");
            var response = await _httpClient.PostAsync("https://api.resend.com/emails", content);

            var responseBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[EmailService] Ответ от Resend: StatusCode={response.StatusCode}");
            Console.WriteLine($"[EmailService] Response Body: {responseBody}");

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EmailService] Error sending email: {ex.Message}");
            Console.WriteLine($"[EmailService] Stack trace: {ex.StackTrace}");
            return false;
        }
    }
}
