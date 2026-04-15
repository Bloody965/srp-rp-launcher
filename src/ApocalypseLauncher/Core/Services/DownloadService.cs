using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ApocalypseLauncher.Core.Services;

public class DownloadService
{
    private readonly HttpClient _httpClient;
    public event EventHandler<int>? ProgressChanged;
    public event EventHandler<string>? StatusChanged;

    public DownloadService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ApocalypseLauncher/1.0");
    }

    public async Task<string> DownloadFileAsync(string url, string destinationPath)
    {
        try
        {
            StatusChanged?.Invoke(this, $"Загрузка: {Path.GetFileName(destinationPath)}");
            Console.WriteLine($"Downloading: {url}");

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var progress = (int)((downloadedBytes * 100) / totalBytes);
                    ProgressChanged?.Invoke(this, progress);
                }
            }

            Console.WriteLine($"Downloaded: {destinationPath}");
            return destinationPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Download error: {ex.Message}");
            StatusChanged?.Invoke(this, $"Ошибка загрузки: {ex.Message}");
            throw;
        }
    }

    public async Task<JObject> DownloadJsonAsync(string url)
    {
        try
        {
            Console.WriteLine($"Downloading JSON: {url}");
            var json = await _httpClient.GetStringAsync(url);
            return JObject.Parse(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"JSON download error: {ex.Message}");
            throw;
        }
    }
}
