using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ApocalypseLauncher.Core.Services;

public class LauncherUpdateService
{
    private const string GITHUB_REPO = "Bloody965/srp-rp-launcher";
    private const string CURRENT_VERSION = "1.0.0";
    private readonly HttpClient _httpClient;
    private readonly string? _apiBaseUrl;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<int>? ProgressChanged;

    public LauncherUpdateService(string? apiBaseUrl = null)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SRP-RP-Launcher");
        _apiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? null : apiBaseUrl.TrimEnd('/');
    }

    public async Task<(bool hasUpdate, string latestVersion, string downloadUrl)> CheckForUpdatesAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_apiBaseUrl))
            {
                var apiUrl = $"{_apiBaseUrl}/api/launcher/version";
                var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);

                // Reuse existing launcher auth token if present.
                var sessionToken = TryReadSessionToken();
                if (!string.IsNullOrWhiteSpace(sessionToken))
                {
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sessionToken);
                }

                using var apiResponse = await _httpClient.SendAsync(request);
                if (apiResponse.IsSuccessStatusCode)
                {
                    var payload = await apiResponse.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(payload);
                    var latestVersionFromApi = doc.RootElement.GetProperty("version").GetString() ?? CURRENT_VERSION;
                    var downloadUrlFromApi = doc.RootElement.GetProperty("downloadUrl").GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(downloadUrlFromApi))
                    {
                        var hasUpdateFromApi = CompareVersions(CURRENT_VERSION, latestVersionFromApi) < 0;
                        return (hasUpdateFromApi, latestVersionFromApi, downloadUrlFromApi);
                    }
                }
            }

            var url = $"https://api.github.com/repos/{GITHUB_REPO}/releases/latest";
            var response = await _httpClient.GetStringAsync(url);
            var release = JsonDocument.Parse(response);

            var tagName = release.RootElement.GetProperty("tag_name").GetString() ?? "";
            var latestVersion = tagName.TrimStart('v');

            // Ищем ApocalypseLauncher.zip в assets
            string? downloadUrl = null;
            if (release.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    if (name == "ApocalypseLauncher.zip")
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            if (downloadUrl == null)
            {
                return (false, CURRENT_VERSION, "");
            }

            var hasUpdate = CompareVersions(CURRENT_VERSION, latestVersion) < 0;
            return (hasUpdate, latestVersion, downloadUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LauncherUpdateService] Ошибка проверки обновлений: {ex.Message}");
            return (false, CURRENT_VERSION, "");
        }
    }

    public async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl)
    {
        try
        {
            StatusChanged?.Invoke(this, "Скачивание обновления...");
            ProgressChanged?.Invoke(this, 0);

            var tempZip = Path.Combine(Path.GetTempPath(), "launcher_update.zip");
            var tempDir = Path.Combine(Path.GetTempPath(), "launcher_update");

            // Скачиваем ZIP
            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progress = (int)((totalRead * 100) / totalBytes);
                        ProgressChanged?.Invoke(this, progress);
                    }
                }
            }

            StatusChanged?.Invoke(this, "Распаковка обновления...");

            // Удаляем старую временную папку если есть
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }

            // Распаковываем
            ZipFile.ExtractToDirectory(tempZip, tempDir);
            File.Delete(tempZip);

            StatusChanged?.Invoke(this, "Применение обновления...");

            // Создаем скрипт обновления
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var currentDir = Path.GetDirectoryName(currentExe) ?? "";
            var updaterScript = Path.Combine(Path.GetTempPath(), "update_launcher.bat");

            var scriptContent = $@"@echo off
timeout /t 2 /nobreak > nul
echo Обновление лаунчера...

xcopy ""{tempDir}\*"" ""{currentDir}"" /E /Y /I
if errorlevel 1 (
    echo Ошибка копирования файлов
    pause
    exit /b 1
)

rd /s /q ""{tempDir}""
echo Обновление завершено!
start """" ""{currentExe}""
del ""%~f0""
";

            File.WriteAllText(updaterScript, scriptContent);

            // Запускаем скрипт обновления
            var psi = new ProcessStartInfo
            {
                FileName = updaterScript,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            Process.Start(psi);

            // Закрываем текущий лаунчер
            Environment.Exit(0);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LauncherUpdateService] Ошибка установки обновления: {ex.Message}");
            StatusChanged?.Invoke(this, $"Ошибка: {ex.Message}");
            return false;
        }
    }

    private int CompareVersions(string current, string latest)
    {
        var currentParts = current.Split('.');
        var latestParts = latest.Split('.');

        for (int i = 0; i < Math.Max(currentParts.Length, latestParts.Length); i++)
        {
            var currentPart = i < currentParts.Length ? int.Parse(currentParts[i]) : 0;
            var latestPart = i < latestParts.Length ? int.Parse(latestParts[i]) : 0;

            if (currentPart < latestPart) return -1;
            if (currentPart > latestPart) return 1;
        }

        return 0;
    }

    private static string TryReadSessionToken()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var tokenFile = Path.Combine(appData, "SRP-RP-Launcher", "session.dat");
            if (!File.Exists(tokenFile))
            {
                return string.Empty;
            }

            var stored = File.ReadAllText(tokenFile);
            if (stored.StartsWith("enc:", StringComparison.Ordinal) && OperatingSystem.IsWindows())
            {
                var payload = stored.Substring(4);
                var protectedBytes = Convert.FromBase64String(payload);
                var bytes = System.Security.Cryptography.ProtectedData.Unprotect(
                    protectedBytes,
                    null,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                stored = System.Text.Encoding.UTF8.GetString(bytes);
            }

            var parts = stored.Split('|');
            return parts.Length >= 1 ? parts[0] : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
