using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ApocalypseLauncher.Core;

namespace ApocalypseLauncher.Core.Services;

public class LauncherUpdateService
{
    private const string GITHUB_REPO = "Bloody965/srp-rp-launcher";
    private static readonly string CURRENT_VERSION = ResolveCurrentVersion();
    private static readonly string UpdateLogPath = ResolveUpdateLogPath();
    private readonly HttpClient _httpClient;
    private readonly string? _apiBaseUrl;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<int>? ProgressChanged;

    public LauncherUpdateService(string? apiBaseUrl = null)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SRP-RP-Launcher");
        _apiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? null : apiBaseUrl.TrimEnd('/');
        WriteLog($"Init: currentVersion={CURRENT_VERSION}, apiBase={_apiBaseUrl ?? "<none>"}");
    }

    public async Task<(bool hasUpdate, string latestVersion, string downloadUrl)> CheckForUpdatesAsync()
    {
        try
        {
            WriteLog("CheckForUpdates: started");
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
                        WriteLog($"CheckForUpdates(API): current={CURRENT_VERSION}, latest={latestVersionFromApi}, hasUpdate={hasUpdateFromApi}");
                        return (hasUpdateFromApi, latestVersionFromApi, downloadUrlFromApi);
                    }
                    WriteLog("CheckForUpdates(API): empty downloadUrl, fallback to GitHub");
                }
                else
                {
                    WriteLog($"CheckForUpdates(API): status={(int)apiResponse.StatusCode}, fallback to GitHub");
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
                WriteLog("CheckForUpdates(GitHub): ApocalypseLauncher.zip not found");
                return (false, CURRENT_VERSION, "");
            }

            var hasUpdate = CompareVersions(CURRENT_VERSION, latestVersion) < 0;
            WriteLog($"CheckForUpdates(GitHub): current={CURRENT_VERSION}, latest={latestVersion}, hasUpdate={hasUpdate}");
            return (hasUpdate, latestVersion, downloadUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LauncherUpdateService] Ошибка проверки обновлений: {ex.Message}");
            WriteLog($"CheckForUpdates: error={ex}");
            return (false, CURRENT_VERSION, "");
        }
    }

    public async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl)
    {
        try
        {
            WriteLog($"InstallUpdate: started, url={downloadUrl}");
            StatusChanged?.Invoke(this, "Скачивание обновления...");
            ProgressChanged?.Invoke(this, 0);

            var uniqueId = Guid.NewGuid().ToString("N");
            var tempZip = Path.Combine(Path.GetTempPath(), $"launcher_update_{uniqueId}.zip");
            var tempDir = Path.Combine(Path.GetTempPath(), $"launcher_update_{uniqueId}");

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
            var updateSourceDir = NormalizeExtractedUpdateRoot(tempDir);
            WriteLog($"InstallUpdate: extracted={tempDir}, source={updateSourceDir}");

            StatusChanged?.Invoke(this, "Применение обновления...");

            // Создаем PowerShell-скрипт обновления (robocopy надежнее xcopy на разных локалях/путях).
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var currentDir = Path.GetDirectoryName(currentExe) ?? "";
            var updaterScript = Path.Combine(Path.GetTempPath(), "update_launcher.ps1");

            static string EscapePs(string value) => value.Replace("'", "''");
            var scriptContent = $@"$ErrorActionPreference = 'Stop'
Start-Sleep -Seconds 2

$source = '{EscapePs(updateSourceDir)}'
$target = '{EscapePs(currentDir)}'
$exe = '{EscapePs(currentExe)}'
$logPath = '{EscapePs(UpdateLogPath)}'

if (-not (Test-Path -LiteralPath $source)) {{
    throw ""Update source folder not found: $source""
}}
if (-not (Test-Path -LiteralPath $target)) {{
    New-Item -ItemType Directory -Path $target -Force | Out-Null
}}

# /E - copy subdirs including empty, /R /W - retry policy, /NFL /NDL - cleaner output
& robocopy $source $target /E /R:3 /W:1 /NFL /NDL /NJH /NJS /NP
$rc = $LASTEXITCODE
if ($rc -ge 8) {{
    Add-Content -LiteralPath $logPath -Value ""[$(Get-Date -Format o)] InstallUpdate(script): robocopy failed rc=$rc source=$source target=$target""
    throw ""Robocopy failed with exit code $rc""
}}
Add-Content -LiteralPath $logPath -Value ""[$(Get-Date -Format o)] InstallUpdate(script): robocopy ok rc=$rc source=$source target=$target""

Remove-Item -LiteralPath $source -Recurse -Force -ErrorAction SilentlyContinue
Start-Process -FilePath $exe -WorkingDirectory $target
Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
";

            File.WriteAllText(updaterScript, scriptContent);

            // Запускаем скрипт обновления
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{updaterScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(psi);
            WriteLog("InstallUpdate: updater script started, shutting down current process");

            // Закрываем текущий лаунчер
            Environment.Exit(0);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LauncherUpdateService] Ошибка установки обновления: {ex.Message}");
            StatusChanged?.Invoke(this, $"Ошибка: {ex.Message}");
            WriteLog($"InstallUpdate: error={ex}");
            return false;
        }
    }

    private int CompareVersions(string current, string latest)
    {
        if (TryCompareAsPackedNumeric(current, latest, out var packedResult))
        {
            return packedResult;
        }

        var currentParts = current.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var latestParts = latest.Split('.', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < Math.Max(currentParts.Length, latestParts.Length); i++)
        {
            var currentPart = i < currentParts.Length && int.TryParse(currentParts[i], out var c) ? c : 0;
            var latestPart = i < latestParts.Length && int.TryParse(latestParts[i], out var l) ? l : 0;

            if (currentPart < latestPart) return -1;
            if (currentPart > latestPart) return 1;
        }

        return 0;
    }

    private static bool TryCompareAsPackedNumeric(string current, string latest, out int result)
    {
        result = 0;
        var currentPackedRaw = current.Replace(".", string.Empty).Trim();
        var latestPackedRaw = latest.Replace(".", string.Empty).Trim();
        if (latestPackedRaw.StartsWith('v') || latestPackedRaw.StartsWith('V'))
        {
            latestPackedRaw = latestPackedRaw[1..];
        }

        if (!int.TryParse(currentPackedRaw, out var currentPacked) ||
            !int.TryParse(latestPackedRaw, out var latestPacked))
        {
            return false;
        }

        result = currentPacked.CompareTo(latestPacked);
        return true;
    }

    private static string ResolveCurrentVersion() => LauncherVersionInfo.GetSemanticVersion();

    private static string ResolveUpdateLogPath()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logsDir = Path.Combine(appData, "SRP-RP-Launcher", "logs");
            Directory.CreateDirectory(logsDir);
            return Path.Combine(logsDir, "launcher_update.log");
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), "launcher_update.log");
        }
    }

    private static void WriteLog(string message)
    {
        try
        {
            File.AppendAllText(UpdateLogPath, $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore logging failures
        }
    }

    private static string NormalizeExtractedUpdateRoot(string extractedDir)
    {
        try
        {
            var files = Directory.GetFiles(extractedDir);
            if (files.Length > 0)
            {
                return extractedDir;
            }

            var subDirs = Directory.GetDirectories(extractedDir);
            if (subDirs.Length == 1)
            {
                return subDirs[0];
            }
        }
        catch
        {
            // ignore and return original
        }

        return extractedDir;
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
