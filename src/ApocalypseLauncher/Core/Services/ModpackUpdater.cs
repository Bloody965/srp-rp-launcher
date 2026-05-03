using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
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

            if (!string.IsNullOrWhiteSpace(modpackInfo.DeltaManifestUrl))
            {
                StatusChanged?.Invoke(this, "Инкрементное обновление сборки...");
                Console.WriteLine($"[ModpackUpdater] Trying delta update from: {modpackInfo.DeltaManifestUrl}");

                if (await TryApplyDeltaUpdateAsync(modpackInfo))
                {
                    await SaveInstalledVersionAsync(modpackInfo.Version);
                    StatusChanged?.Invoke(this, "Сборка обновлена (только изменённые файлы)");
                    Console.WriteLine("[ModpackUpdater] Delta update applied successfully");
                    return true;
                }

                StatusChanged?.Invoke(this, "Инкрементное обновление недоступно, загружаем полный архив...");
                Console.WriteLine("[ModpackUpdater] Delta update failed, fallback to full ZIP");
            }

            var fullInstallOk = await DownloadAndInstallFullArchiveAsync(modpackInfo);
            if (fullInstallOk)
            {
                await SaveInstalledVersionAsync(modpackInfo.Version);
                StatusChanged?.Invoke(this, "Сборка установлена!");
                Console.WriteLine("[ModpackUpdater] Full archive installed successfully");
            }

            return fullInstallOk;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Ошибка: {ex.Message}");
            Console.WriteLine($"[ModpackUpdater] ERROR: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    private async Task<bool> DownloadAndInstallFullArchiveAsync(ModpackInfo modpackInfo)
    {
        StatusChanged?.Invoke(this, "Скачивание сборки...");
        Console.WriteLine("[ModpackUpdater] Downloading modpack...");

        var tempZip = Path.Combine(Path.GetTempPath(), "modpack.zip");

        using (var httpClient = CreateDownloadHttpClient(modpackInfo.DownloadUrl))
        using (var response = await httpClient.GetAsync(modpackInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            if (!response.IsSuccessStatusCode)
            {
                StatusChanged?.Invoke(this, "Ошибка скачивания сборки");
                return false;
            }

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
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

        Console.WriteLine($"[ModpackUpdater] Downloaded to: {tempZip}");

        if (!IsZipFile(tempZip))
        {
            var sourceHost = TryGetHost(modpackInfo.DownloadUrl);
            StatusChanged?.Invoke(this, $"Ошибка: файл не ZIP. Источник: {sourceHost}. Проверьте Modpack:DownloadUrl на сервере.");
            try { File.Delete(tempZip); } catch { }
            return false;
        }

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

        StatusChanged?.Invoke(this, "Установка сборки...");
        Console.WriteLine("[ModpackUpdater] Extracting modpack...");
        ZipFile.ExtractToDirectory(tempZip, _minecraftDirectory, true);
        try { File.Delete(tempZip); } catch { }
        return true;
    }

    private async Task<bool> TryApplyDeltaUpdateAsync(ModpackInfo modpackInfo)
    {
        if (string.IsNullOrWhiteSpace(modpackInfo.DeltaManifestUrl))
        {
            return false;
        }

        try
        {
            StatusChanged?.Invoke(this, "Загрузка манифеста обновлений...");
            using var manifestClient = CreateDownloadHttpClient(modpackInfo.DeltaManifestUrl);
            using var manifestResponse = await manifestClient.GetAsync(modpackInfo.DeltaManifestUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!manifestResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"[ModpackUpdater] Delta manifest HTTP {(int)manifestResponse.StatusCode}");
                return false;
            }

            await using var manifestStream = await manifestResponse.Content.ReadAsStreamAsync();
            var manifest = await JsonSerializer.DeserializeAsync<DeltaManifest>(manifestStream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (manifest?.Files == null || manifest.Files.Count == 0)
            {
                Console.WriteLine("[ModpackUpdater] Delta manifest has no files");
                return false;
            }

            var changedFiles = new List<DeltaManifestFile>();
            foreach (var file in manifest.Files)
            {
                var safePath = NormalizeRelativePath(file.Path);
                var expectedHash = file.GetExpectedHash();
                if (string.IsNullOrWhiteSpace(safePath) || string.IsNullOrWhiteSpace(expectedHash))
                {
                    continue;
                }

                var localPath = Path.Combine(_minecraftDirectory, safePath);
                if (!File.Exists(localPath))
                {
                    changedFiles.Add(file);
                    continue;
                }

                var localHash = CalculateSHA256(localPath);
                if (!localHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    changedFiles.Add(file);
                }
            }

            var totalBytes = changedFiles.Sum(f => f.GetExpectedSizeBytes());
            var downloadedBytes = 0L;

            if (changedFiles.Count == 0)
            {
                ProgressChanged?.Invoke(this, 100);
            }

            foreach (var file in changedFiles)
            {
                var safePath = NormalizeRelativePath(file.Path);
                if (string.IsNullOrWhiteSpace(safePath))
                {
                    return false;
                }

                var fileUrl = ResolveManifestFileUrl(file, manifest, modpackInfo.DeltaManifestUrl);
                if (string.IsNullOrWhiteSpace(fileUrl))
                {
                    Console.WriteLine($"[ModpackUpdater] Missing URL for file {safePath}");
                    return false;
                }

                StatusChanged?.Invoke(this, $"Обновление: {safePath}");

                var destination = Path.Combine(_minecraftDirectory, safePath);
                var destinationDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                var tempDownloadPath = destination + ".download";
                using (var fileClient = CreateDownloadHttpClient(fileUrl))
                using (var fileResponse = await fileClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!fileResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[ModpackUpdater] Failed to download {safePath}: {(int)fileResponse.StatusCode}");
                        return false;
                    }

                    await using var remoteStream = await fileResponse.Content.ReadAsStreamAsync();
                    await using var localStream = new FileStream(tempDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                    await remoteStream.CopyToAsync(localStream);
                }

                var expectedHash = file.GetExpectedHash();
                var downloadedHash = CalculateSHA256(tempDownloadPath);
                if (!downloadedHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[ModpackUpdater] Delta hash mismatch for {safePath}");
                    try { File.Delete(tempDownloadPath); } catch { }
                    return false;
                }

                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                File.Move(tempDownloadPath, destination);
                downloadedBytes += file.GetExpectedSizeBytes();

                if (totalBytes > 0)
                {
                    var progress = (int)((downloadedBytes * 100) / totalBytes);
                    ProgressChanged?.Invoke(this, Math.Clamp(progress, 0, 100));
                }
                else
                {
                    var progress = (int)(((double)(changedFiles.IndexOf(file) + 1) / changedFiles.Count) * 100);
                    ProgressChanged?.Invoke(this, Math.Clamp(progress, 0, 100));
                }
            }

            if (manifest.DeletePaths != null)
            {
                foreach (var deletePath in manifest.DeletePaths)
                {
                    var safeDeletePath = NormalizeRelativePath(deletePath);
                    if (string.IsNullOrWhiteSpace(safeDeletePath))
                    {
                        continue;
                    }

                    var absoluteDeletePath = Path.Combine(_minecraftDirectory, safeDeletePath);
                    if (File.Exists(absoluteDeletePath))
                    {
                        File.Delete(absoluteDeletePath);
                    }
                    else if (Directory.Exists(absoluteDeletePath))
                    {
                        Directory.Delete(absoluteDeletePath, true);
                    }
                }
            }

            ProgressChanged?.Invoke(this, 100);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModpackUpdater] Delta update error: {ex.Message}");
            return false;
        }
    }

    private async Task SaveInstalledVersionAsync(string version)
    {
        var versionFile = Path.Combine(_minecraftDirectory, "modpack_version.txt");
        await File.WriteAllTextAsync(versionFile, version);
        Console.WriteLine($"[ModpackUpdater] Updated to version: {version}");
    }

    private HttpClient CreateDownloadHttpClient(string downloadUrl)
    {
        var httpClient = new HttpClient();
        var authToken = _apiService.GetCurrentAuthToken();
        if (!string.IsNullOrWhiteSpace(authToken) && ShouldAttachAuthHeader(downloadUrl))
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);
        }

        return httpClient;
    }

    private static string ResolveManifestFileUrl(DeltaManifestFile file, DeltaManifest manifest, string manifestUrl)
    {
        if (!string.IsNullOrWhiteSpace(file.Url))
        {
            if (Uri.TryCreate(file.Url, UriKind.Absolute, out var absFileUrl))
            {
                return absFileUrl.ToString();
            }

            if (!string.IsNullOrWhiteSpace(manifest.BaseUrl) &&
                Uri.TryCreate(manifest.BaseUrl, UriKind.Absolute, out var absBase))
            {
                return new Uri(absBase, file.Url).ToString();
            }

            if (Uri.TryCreate(manifestUrl, UriKind.Absolute, out var manifestUri))
            {
                return new Uri(manifestUri, file.Url).ToString();
            }
        }

        return string.Empty;
    }

    private static string NormalizeRelativePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        var normalized = rawPath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return normalized.Replace('/', Path.DirectorySeparatorChar);
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

    private static bool IsZipFile(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            if (stream.Length < 4)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[4];
            var read = stream.Read(header);
            if (read < 4)
            {
                return false;
            }

            return header[0] == (byte)'P' &&
                   header[1] == (byte)'K' &&
                   (header[2] == 3 || header[2] == 5 || header[2] == 7) &&
                   (header[3] == 4 || header[3] == 6 || header[3] == 8);
        }
        catch
        {
            return false;
        }
    }

    private bool ShouldAttachAuthHeader(string downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var targetUri))
        {
            return true;
        }

        var apiBase = _apiService.GetBaseAddress();
        if (apiBase == null)
        {
            return false;
        }

        return string.Equals(targetUri.Host, apiBase.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static string TryGetHost(string downloadUrl)
    {
        if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        return "relative-url";
    }

    private sealed class DeltaManifest
    {
        public string? Version { get; set; }
        public string? BaseUrl { get; set; }
        public List<DeltaManifestFile> Files { get; set; } = new();
        public List<string> DeletePaths { get; set; } = new();
    }

    private sealed class DeltaManifestFile
    {
        public string Path { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public string Sha256Hash { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public long FileSizeBytes { get; set; }

        public string GetExpectedHash()
        {
            return !string.IsNullOrWhiteSpace(Sha256Hash) ? Sha256Hash : Sha256;
        }

        public long GetExpectedSizeBytes()
        {
            return FileSizeBytes > 0 ? FileSizeBytes : SizeBytes;
        }
    }
}
