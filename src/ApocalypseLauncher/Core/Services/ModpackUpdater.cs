using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace ApocalypseLauncher.Core.Services;

public class ModpackUpdater
{
    private readonly string _minecraftDirectory;
    private readonly ApiService _apiService;

    public event EventHandler<string> StatusChanged;
    public event EventHandler<int> ProgressChanged;

    public ModpackUpdater(string minecraftDirectory, ApiService apiService)
    {
        _minecraftDirectory = minecraftDirectory;
        _apiService = apiService;
    }

    public async Task<string> GetCurrentVersionAsync()
    {
        var versionFile = Path.Combine(_minecraftDirectory, "modpack_version.txt");
        if (File.Exists(versionFile))
        {
            return await File.ReadAllTextAsync(versionFile);
        }
        return "0.0.0";
    }

    public async Task<string> GetLatestVersionAsync()
    {
        try
        {
            StatusChanged?.Invoke(this, "Проверка обновлений...");
            var result = await _apiService.GetModpackVersionAsync();

            if (result.IsSuccess && result.Data != null)
            {
                return result.Data.Version;
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModpackUpdater] Error checking version: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> CheckForUpdatesAsync()
    {
        var currentVersion = await GetCurrentVersionAsync();
        var latestVersion = await GetLatestVersionAsync();

        if (latestVersion == null)
            return false;

        Console.WriteLine($"[ModpackUpdater] Current: {currentVersion}, Latest: {latestVersion}");
        return latestVersion != currentVersion;
    }

    public async Task<bool> DownloadAndInstallModpackAsync()
    {
        try
        {
            StatusChanged?.Invoke(this, "Получение информации о сборке...");
            Console.WriteLine("[ModpackUpdater] Getting modpack info...");

            var result = await _apiService.GetModpackVersionAsync();

            if (!result.IsSuccess || result.Data == null)
            {
                StatusChanged?.Invoke(this, "Ошибка получения информации о сборке");
                return false;
            }

            var modpackInfo = result.Data;

            StatusChanged?.Invoke(this, "Скачивание сборки...");
            Console.WriteLine("[ModpackUpdater] Downloading modpack...");

            var tempZip = Path.Combine(Path.GetTempPath(), "modpack.zip");

            // Скачиваем ZIP через защищенный API
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GetAuthToken());

                using (var response = await httpClient.GetAsync(modpackInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        StatusChanged?.Invoke(this, "Ошибка скачивания сборки");
                        return false;
                    }

                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
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
                }
            }

            Console.WriteLine($"[ModpackUpdater] Downloaded to: {tempZip}");

            // Проверка SHA256 хеша (пропускаем если хеш = "skip")
            if (!modpackInfo.SHA256Hash.Equals("skip", StringComparison.OrdinalIgnoreCase))
            {
                StatusChanged?.Invoke(this, "Проверка целостности файла...");
                var fileHash = CalculateSHA256(tempZip);

                if (!fileHash.Equals(modpackInfo.SHA256Hash, StringComparison.OrdinalIgnoreCase))
                {
                    StatusChanged?.Invoke(this, "Ошибка: файл поврежден!");
                    Console.WriteLine($"[ModpackUpdater] Hash mismatch! Expected: {modpackInfo.SHA256Hash}, Got: {fileHash}");
                    try { File.Delete(tempZip); } catch { }
                    return false;
                }

                Console.WriteLine("[ModpackUpdater] Hash verified successfully");
            }
            else
            {
                Console.WriteLine("[ModpackUpdater] Hash verification skipped");
            }

            // Удаляем старые моды и конфиги
            StatusChanged?.Invoke(this, "Удаление старых файлов...");
            var modsDir = Path.Combine(_minecraftDirectory, "mods");
            var configDir = Path.Combine(_minecraftDirectory, "config");

            if (Directory.Exists(modsDir))
            {
                Console.WriteLine("[ModpackUpdater] Removing old mods...");
                Directory.Delete(modsDir, true);
            }

            if (Directory.Exists(configDir))
            {
                Console.WriteLine("[ModpackUpdater] Removing old config...");
                Directory.Delete(configDir, true);
            }

            // Распаковываем новую сборку
            StatusChanged?.Invoke(this, "Установка сборки...");
            Console.WriteLine("[ModpackUpdater] Extracting modpack...");
            ZipFile.ExtractToDirectory(tempZip, _minecraftDirectory, true);

            // Сохраняем новую версию
            var versionFile = Path.Combine(_minecraftDirectory, "modpack_version.txt");
            await File.WriteAllTextAsync(versionFile, modpackInfo.Version);
            Console.WriteLine($"[ModpackUpdater] Updated to version: {modpackInfo.Version}");

            // Удаляем временный файл
            try { File.Delete(tempZip); } catch { }

            StatusChanged?.Invoke(this, "Сборка установлена!");
            Console.WriteLine("[ModpackUpdater] Modpack installed successfully!");
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Ошибка: {ex.Message}");
            Console.WriteLine($"[ModpackUpdater] ERROR: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    private string CalculateSHA256(string filePath)
    {
        using (var sha256 = SHA256.Create())
        using (var stream = File.OpenRead(filePath))
        {
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    private string GetAuthToken()
    {
        // Токен будет установлен в ApiService после входа
        return "";
    }
}
