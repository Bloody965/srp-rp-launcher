using SendGrid;
using SendGrid.Helpers.Mail;

namespace ApocalypseLauncher.API.Services;

public class EmailService
{
    private readonly string _apiKey;
    private readonly string _fromEmail;
    private readonly string _fromName;

    public EmailService(IConfiguration configuration)
    {
        _apiKey = configuration["SendGrid:ApiKey"] ?? throw new Exception("SendGrid API key not configured");
        _fromEmail = configuration["SendGrid:FromEmail"] ?? throw new Exception("SendGrid FromEmail not configured");
        _fromName = configuration["SendGrid:FromName"] ?? "SRP-RP Launcher";
    }

    public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string username, string resetCode)
    {
        try
        {
            var client = new SendGridClient(_apiKey);
            var from = new EmailAddress(_fromEmail, _fromName);
            var to = new EmailAddress(toEmail);
            var subject = "Сброс пароля - SRP-RP Launcher";

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

            var plainTextContent = $@"
SRP-RP LAUNCHER - Сброс пароля

Привет, {username}!

Вы запросили сброс пароля для вашего аккаунта.

Ваш код для сброса пароля: {resetCode}

Введите этот код в лаунчере для сброса пароля.
Код действителен в течение 15 минут.

Если вы не запрашивали сброс пароля, просто проигнорируйте это письмо.

POST-APOCALYPSE RESIDENT RP | 2026
            ";

            var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);
            var response = await client.SendEmailAsync(msg);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EmailService] Error sending email: {ex.Message}");
            return false;
        }
    }
}
