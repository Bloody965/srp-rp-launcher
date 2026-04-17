using System;
using System.IO;
using System.Threading.Tasks;

namespace ApocalypseLauncher.Core.Services;

public class SkinService
{
    private readonly ApiService _apiService;
    private readonly string _skinsDirectory;
    private readonly string _capesDirectory;

    public event EventHandler<string>? StatusChanged;

    public SkinService(ApiService apiService, string minecraftDirectory)
    {
        _apiService = apiService;
        _skinsDirectory = Path.Combine(minecraftDirectory, "assets", "skins");
        _capesDirectory = Path.Combine(minecraftDirectory, "assets", "capes");

        Directory.CreateDirectory(_skinsDirectory);
        Directory.CreateDirectory(_capesDirectory);
    }

    /// <summary>
    /// Загрузить скин на сервер
    /// </summary>
    public async Task<bool> UploadSkinAsync(string filePath, string skinType)
    {
        try
        {
            StatusChanged?.Invoke(this, "Проверка файла скина...");

            // Валидация файла
            if (!ValidateSkinFile(filePath, out var error))
            {
                StatusChanged?.Invoke(this, $"Ошибка: {error}");
                return false;
            }

            StatusChanged?.Invoke(this, "Загрузка скина на сервер...");

            // Читаем файл
            var skinData = await File.ReadAllBytesAsync(filePath);

            // Загружаем на сервер
            var result = await _apiService.UploadSkinAsync(skinData, skinType);

            if (result.IsSuccess)
            {
                StatusChanged?.Invoke(this, "Скин успешно загружен!");
                return true;
            }
            else
            {
                StatusChanged?.Invoke(this, $"Ошибка: {result.ErrorMessage}");
                return false;
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Ошибка: {ex.Message}");
            Console.WriteLine($"[SkinService] Upload error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Загрузить плащ на сервер
    /// </summary>
    public async Task<bool> UploadCapeAsync(string filePath)
    {
        try
        {
            StatusChanged?.Invoke(this, "Проверка файла плаща...");

            // Валидация файла
            if (!ValidateCapeFile(filePath, out var error))
            {
                StatusChanged?.Invoke(this, $"Ошибка: {error}");
                return false;
            }

            StatusChanged?.Invoke(this, "Загрузка плаща на сервер...");

            // Читаем файл
            var capeData = await File.ReadAllBytesAsync(filePath);

            // Загружаем на сервер
            var result = await _apiService.UploadCapeAsync(capeData);

            if (result.IsSuccess)
            {
                StatusChanged?.Invoke(this, "Плащ успешно загружен!");
                return true;
            }
            else
            {
                StatusChanged?.Invoke(this, $"Ошибка: {result.ErrorMessage}");
                return false;
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Ошибка: {ex.Message}");
            Console.WriteLine($"[SkinService] Upload cape error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Скачать и применить скин для текущего пользователя
    /// </summary>
    public async Task<bool> DownloadAndApplySkinAsync(int userId, string username)
    {
        try
        {
            StatusChanged?.Invoke(this, "Скачивание скина...");

            var skinData = await _apiService.DownloadSkinAsync(userId);

            if (skinData == null || skinData.Length == 0)
            {
                StatusChanged?.Invoke(this, "Скин не найден на сервере");
                return false;
            }

            // Сохраняем скин локально
            var skinPath = Path.Combine(_skinsDirectory, $"{username}.png");
            await File.WriteAllBytesAsync(skinPath, skinData);

            StatusChanged?.Invoke(this, "Скин применен!");
            Console.WriteLine($"[SkinService] Skin saved to: {skinPath}");
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Ошибка: {ex.Message}");
            Console.WriteLine($"[SkinService] Download error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Скачать и применить плащ для текущего пользователя
    /// </summary>
    public async Task<bool> DownloadAndApplyCapeAsync(int userId, string username)
    {
        try
        {
            StatusChanged?.Invoke(this, "Скачивание плаща...");

            var capeData = await _apiService.DownloadCapeAsync(userId);

            if (capeData == null || capeData.Length == 0)
            {
                StatusChanged?.Invoke(this, "Плащ не найден на сервере");
                return false;
            }

            // Сохраняем плащ локально
            var capePath = Path.Combine(_capesDirectory, $"{username}.png");
            await File.WriteAllBytesAsync(capePath, capeData);

            StatusChanged?.Invoke(this, "Плащ применен!");
            Console.WriteLine($"[SkinService] Cape saved to: {capePath}");
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Ошибка: {ex.Message}");
            Console.WriteLine($"[SkinService] Download cape error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Удалить текущий скин
    /// </summary>
    public async Task<bool> DeleteCurrentSkinAsync()
    {
        try
        {
            StatusChanged?.Invoke(this, "Удаление скина...");

            var result = await _apiService.DeleteCurrentSkinAsync();

            if (result.IsSuccess)
            {
                StatusChanged?.Invoke(this, "Скин удален!");
                return true;
            }
            else
            {
                StatusChanged?.Invoke(this, $"Ошибка: {result.ErrorMessage}");
                return false;
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Ошибка: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Валидация файла скина
    /// </summary>
    public bool ValidateSkinFile(string filePath, out string? error)
    {
        error = null;

        if (!File.Exists(filePath))
        {
            error = "Файл не найден";
            return false;
        }

        var fileInfo = new FileInfo(filePath);

        // Проверка расширения
        if (fileInfo.Extension.ToLower() != ".png")
        {
            error = "Файл должен быть в формате PNG";
            return false;
        }

        // Проверка размера файла (макс 1 MB)
        if (fileInfo.Length > 1024 * 1024)
        {
            error = "Размер файла не должен превышать 1 MB";
            return false;
        }

        if (fileInfo.Length < 100)
        {
            error = "Файл слишком маленький";
            return false;
        }

        // Базовая проверка PNG signature
        using var stream = File.OpenRead(filePath);
        var signature = new byte[8];
        stream.Read(signature, 0, 8);

        var pngSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        for (int i = 0; i < 8; i++)
        {
            if (signature[i] != pngSignature[i])
            {
                error = "Файл не является корректным PNG";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Валидация файла плаща
    /// </summary>
    public bool ValidateCapeFile(string filePath, out string? error)
    {
        // Используем ту же валидацию что и для скина
        return ValidateSkinFile(filePath, out error);
    }

    /// <summary>
    /// Получить путь к локальному скину
    /// </summary>
    public string GetLocalSkinPath(string username)
    {
        return Path.Combine(_skinsDirectory, $"{username}.png");
    }

    /// <summary>
    /// Проверить есть ли локальный скин
    /// </summary>
    public bool HasLocalSkin(string username)
    {
        return File.Exists(GetLocalSkinPath(username));
    }
}
