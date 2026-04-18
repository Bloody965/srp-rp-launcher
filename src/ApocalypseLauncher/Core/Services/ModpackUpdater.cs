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
            StatusChanged?.Invoke(this, "РџСЂРѕРІРµСЂРєР° РѕР±РЅРѕРІР»РµРЅРёР№...");
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
            StatusChanged?.Invoke(this, "РџРѕР»СѓС‡РµРЅРёРµ РёРЅС„РѕСЂРјР°С†РёРё Рѕ СЃР±РѕСЂРєРµ...");
            Console.WriteLine("[ModpackUpdater] Getting modpack info...");

            var result = await _apiService.GetModpackVersionAsync();

            if (!result.IsSuccess || result.Data == null)
            {
                StatusChanged?.Invoke(this, "РћС€РёР±РєР° РїРѕР»СѓС‡РµРЅРёСЏ РёРЅС„РѕСЂРјР°С†РёРё Рѕ СЃР±РѕСЂРєРµ");
                return false;
            }

            var modpackInfo = result.Data;

            StatusChanged?.Invoke(this, "РЎРєР°С‡РёРІР°РЅРёРµ СЃР±РѕСЂРєРё...");
            Console.WriteLine("[ModpackUpdater] Downloading modpack...");

            var tempZip = Path.Combine(Path.GetTempPath(), "modpack.zip");

            // РЎРєР°С‡РёРІР°РµРј ZIP С‡РµСЂРµР· Р·Р°С‰РёС‰РµРЅРЅС‹Р№ API
            using (var httpClient = new HttpClient())
            {
                var authToken = GetAuthToken();
                if (string.IsNullOrWhiteSpace(authToken))
                {
                    StatusChanged?.Invoke(this, "Ошибка авторизации при загрузке сборки");
                    return false;
                }

                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);

                using (var response = await httpClient.GetAsync(modpackInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        StatusChanged?.Invoke(this, "РћС€РёР±РєР° СЃРєР°С‡РёРІР°РЅРёСЏ СЃР±РѕСЂРєРё");
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

            // РџСЂРѕРІРµСЂРєР° SHA256 С…РµС€Р°
            if (!string.IsNullOrWhiteSpace(modpackInfo.SHA256Hash))
            {
                StatusChanged?.Invoke(this, "РџСЂРѕРІРµСЂРєР° С†РµР»РѕСЃС‚РЅРѕСЃС‚Рё С„Р°Р№Р»Р°...");
                var fileHash = CalculateSHA256(tempZip);

                if (!fileHash.Equals(modpackInfo.SHA256Hash, StringComparison.OrdinalIgnoreCase))
                {
                    StatusChanged?.Invoke(this, "РћС€РёР±РєР°: С„Р°Р№Р» РїРѕРІСЂРµР¶РґРµРЅ!");
                    Console.WriteLine($"[ModpackUpdater] Hash mismatch! Expected: {modpackInfo.SHA256Hash}, Got: {fileHash}");
                    try { File.Delete(tempZip); } catch { }
                    return false;
                }

                Console.WriteLine("[ModpackUpdater] Hash verified successfully");
            }
            else
            {
                StatusChanged?.Invoke(this, "Ошибка: для сборки не задан SHA256 хеш");
                try { File.Delete(tempZip); } catch { }
                return false;
            }

            // РЈРґР°Р»СЏРµРј СЃС‚Р°СЂС‹Рµ РјРѕРґС‹ Рё РєРѕРЅС„РёРіРё
            StatusChanged?.Invoke(this, "РЈРґР°Р»РµРЅРёРµ СЃС‚Р°СЂС‹С… С„Р°Р№Р»РѕРІ...");
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

            // Р Р°СЃРїР°РєРѕРІС‹РІР°РµРј РЅРѕРІСѓСЋ СЃР±РѕСЂРєСѓ
            StatusChanged?.Invoke(this, "РЈСЃС‚Р°РЅРѕРІРєР° СЃР±РѕСЂРєРё...");
            Console.WriteLine("[ModpackUpdater] Extracting modpack...");
            ZipFile.ExtractToDirectory(tempZip, _minecraftDirectory, true);

            // РЎРѕС…СЂР°РЅСЏРµРј РЅРѕРІСѓСЋ РІРµСЂСЃРёСЋ
            var versionFile = Path.Combine(_minecraftDirectory, "modpack_version.txt");
            await File.WriteAllTextAsync(versionFile, modpackInfo.Version);
            Console.WriteLine($"[ModpackUpdater] Updated to version: {modpackInfo.Version}");

            // РЈРґР°Р»СЏРµРј РІСЂРµРјРµРЅРЅС‹Р№ С„Р°Р№Р»
            try { File.Delete(tempZip); } catch { }

            StatusChanged?.Invoke(this, "РЎР±РѕСЂРєР° СѓСЃС‚Р°РЅРѕРІР»РµРЅР°!");
            Console.WriteLine("[ModpackUpdater] Modpack installed successfully!");
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"РћС€РёР±РєР°: {ex.Message}");
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
        return _apiService.GetAuthToken() ?? "";
    }
}

