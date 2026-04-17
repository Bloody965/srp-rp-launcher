using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ApocalypseLauncher.Core.Models;

namespace ApocalypseLauncher.Core.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;
    private string? _authToken;

    public ApiService(string baseUrl = "http://localhost:5000")
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
            AllowAutoRedirect = false
        };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };

        Console.WriteLine($"[ApiService] Initialized with base URL: {baseUrl}");
    }

    public void SetAuthToken(string token)
    {
        _authToken = token;
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<ApiResponse<AuthResult>> RegisterAsync(string username, string password)
    {
        try
        {
            Console.WriteLine($"[ApiService.RegisterAsync] Starting request to {_httpClient.BaseAddress}api/auth/register");
            var request = new { username, password };
            var response = await _httpClient.PostAsJsonAsync("/api/auth/register", request);

            Console.WriteLine($"[ApiService.RegisterAsync] Response status: {response.StatusCode}");
            Console.WriteLine($"[ApiService.RegisterAsync] Response URL: {response.RequestMessage?.RequestUri}");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
                if (result?.Success == true && result.Token != null)
                {
                    SetAuthToken(result.Token);
                    return ApiResponse<AuthResult>.Success(new AuthResult
                    {
                        Token = result.Token,
                        Username = result.User?.Username ?? username,
                        Email = result.User?.Email ?? "",
                        MinecraftUUID = result.User?.MinecraftUUID ?? "",
                        UUID = result.User?.MinecraftUUID ?? "",
                        AccessToken = result.Token,
                        IsOffline = false,
                        RecoveryCode = result.RecoveryCode // Код восстановления
                    });
                }
                return ApiResponse<AuthResult>.Failure(result?.Message ?? "Ошибка регистрации");
            }

            var error = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
            return ApiResponse<AuthResult>.Failure(error?.Message ?? "Ошибка регистрации");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ApiService.RegisterAsync] Exception: {ex.GetType().Name}");
            Console.WriteLine($"[ApiService.RegisterAsync] Message: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"[ApiService.RegisterAsync] Inner: {ex.InnerException.Message}");
            }
            return ApiResponse<AuthResult>.Failure($"Ошибка подключения: {ex.Message}");
        }
    }

    public async Task<ApiResponse<AuthResult>> LoginAsync(string username, string password)
    {
        try
        {
            var request = new { username, password };
            var response = await _httpClient.PostAsJsonAsync("/api/auth/login", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
                if (result?.Success == true && result.Token != null)
                {
                    SetAuthToken(result.Token);
                    return ApiResponse<AuthResult>.Success(new AuthResult
                    {
                        Token = result.Token,
                        Username = result.User?.Username ?? username,
                        Email = result.User?.Email ?? "",
                        MinecraftUUID = result.User?.MinecraftUUID ?? "",
                        UUID = result.User?.MinecraftUUID ?? "",
                        AccessToken = result.Token,
                        IsOffline = false
                    });
                }
                return ApiResponse<AuthResult>.Failure(result?.Message ?? "Ошибка входа");
            }

            var error = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
            return ApiResponse<AuthResult>.Failure(error?.Message ?? "Неверное имя пользователя или пароль");
        }
        catch (Exception ex)
        {
            return ApiResponse<AuthResult>.Failure($"Ошибка подключения: {ex.Message}");
        }
    }

    public async Task<ApiResponse<bool>> VerifyTokenAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("/api/auth/verify", null);

            if (response.IsSuccessStatusCode)
            {
                return ApiResponse<bool>.Success(true);
            }

            return ApiResponse<bool>.Failure("Токен недействителен");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.Failure($"Ошибка проверки: {ex.Message}");
        }
    }

    public async Task<ApiResponse<ModpackInfo>> GetModpackVersionAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/modpack/version");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ModpackInfoDto>();
                if (result != null)
                {
                    return ApiResponse<ModpackInfo>.Success(new ModpackInfo
                    {
                        Version = result.Version,
                        DownloadUrl = result.DownloadUrl,
                        SHA256Hash = result.SHA256Hash,
                        FileSizeBytes = result.FileSizeBytes,
                        Changelog = result.Changelog
                    });
                }
            }

            return ApiResponse<ModpackInfo>.Failure("Не удалось получить информацию о сборке");
        }
        catch (Exception ex)
        {
            return ApiResponse<ModpackInfo>.Failure($"Ошибка: {ex.Message}");
        }
    }

    public async Task<ApiResponse<string>> RequestResetCodeAsync(string email)
    {
        try
        {
            var request = new { email };
            var response = await _httpClient.PostAsJsonAsync("/api/auth/request-reset-code", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
                if (result?.Success == true)
                {
                    return ApiResponse<string>.Success(result.Message ?? "Код отправлен на почту");
                }
                return ApiResponse<string>.Failure(result?.Message ?? "Ошибка отправки кода");
            }

            var error = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
            return ApiResponse<string>.Failure(error?.Message ?? "Ошибка отправки кода");
        }
        catch (Exception ex)
        {
            return ApiResponse<string>.Failure($"Ошибка подключения: {ex.Message}");
        }
    }

    public async Task<ApiResponse<string>> ResetPasswordAsync(string email, string code, string newPassword)
    {
        try
        {
            var request = new { email, code, newPassword };
            var response = await _httpClient.PostAsJsonAsync("/api/auth/reset-password", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
                if (result?.Success == true)
                {
                    return ApiResponse<string>.Success(result.Message ?? "Пароль успешно изменен");
                }
                return ApiResponse<string>.Failure(result?.Message ?? "Ошибка сброса пароля");
            }

            var error = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
            return ApiResponse<string>.Failure(error?.Message ?? "Ошибка сброса пароля");
        }
        catch (Exception ex)
        {
            return ApiResponse<string>.Failure($"Ошибка подключения: {ex.Message}");
        }
    }

    public async Task<ApiResponse<ProfileInfo>> GetProfileAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/auth/profile");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ProfileResponseDto>();
                if (result?.Success == true)
                {
                    return ApiResponse<ProfileInfo>.Success(new ProfileInfo
                    {
                        Username = result.Username,
                        Email = result.Email,
                        PlayTimeMinutes = result.PlayTimeMinutes,
                        CreatedAt = result.CreatedAt,
                        LastLoginAt = result.LastLoginAt
                    });
                }
            }

            return ApiResponse<ProfileInfo>.Failure("Не удалось получить профиль");
        }
        catch (Exception ex)
        {
            return ApiResponse<ProfileInfo>.Failure($"Ошибка: {ex.Message}");
        }
    }

    public async Task<ApiResponse<string>> ChangeUsernameAsync(string newUsername)
    {
        try
        {
            var request = new { newUsername };
            var response = await _httpClient.PostAsJsonAsync("/api/auth/change-username", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
                if (result?.Success == true)
                {
                    return ApiResponse<string>.Success(result.Message ?? "Никнейм изменен");
                }
                return ApiResponse<string>.Failure(result?.Message ?? "Ошибка смены никнейма");
            }

            var error = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
            return ApiResponse<string>.Failure(error?.Message ?? "Ошибка смены никнейма");
        }
        catch (Exception ex)
        {
            return ApiResponse<string>.Failure($"Ошибка подключения: {ex.Message}");
        }
    }

    public async Task<ApiResponse<bool>> UpdatePlayTimeAsync(int minutesPlayed)
    {
        try
        {
            var request = new { minutesPlayed };
            var response = await _httpClient.PostAsJsonAsync("/api/auth/update-playtime", request);

            if (response.IsSuccessStatusCode)
            {
                return ApiResponse<bool>.Success(true);
            }

            return ApiResponse<bool>.Failure("Не удалось обновить игровое время");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.Failure($"Ошибка: {ex.Message}");
        }
    }

    public async Task<ApiResponse<ServerStatus>> GetServerStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/server/status");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ServerStatusDto>();
                if (result != null)
                {
                    return ApiResponse<ServerStatus>.Success(new ServerStatus
                    {
                        IsOnline = result.IsOnline,
                        PlayersOnline = result.PlayersOnline,
                        MaxPlayers = result.MaxPlayers,
                        ServerVersion = result.ServerVersion,
                        Motd = result.Motd
                    });
                }
            }

            return ApiResponse<ServerStatus>.Failure("Не удалось получить статус сервера");
        }
        catch (Exception ex)
        {
            return ApiResponse<ServerStatus>.Failure($"Ошибка: {ex.Message}");
        }
    }

    // Skins API
    public async Task<ApiResponse<SkinInfo>> UploadSkinAsync(byte[] skinData, string skinType)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(skinData);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "skin", "skin.png");
            content.Add(new StringContent(skinType), "skinType");

            var response = await _httpClient.PostAsync("/api/skins/upload", content);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SkinUploadResponse>();
                if (result?.Success == true && result.Skin != null)
                {
                    return ApiResponse<SkinInfo>.Success(new SkinInfo
                    {
                        SkinType = result.Skin.SkinType,
                        DownloadUrl = result.Skin.DownloadUrl,
                        FileHash = result.Skin.FileHash,
                        UploadedAt = result.Skin.UploadedAt
                    });
                }
                return ApiResponse<SkinInfo>.Failure(result?.Message ?? "Ошибка загрузки скина");
            }

            var error = await response.Content.ReadFromJsonAsync<SkinUploadResponse>();
            return ApiResponse<SkinInfo>.Failure(error?.Message ?? "Ошибка загрузки скина");
        }
        catch (Exception ex)
        {
            return ApiResponse<SkinInfo>.Failure($"Ошибка подключения: {ex.Message}");
        }
    }

    public async Task<ApiResponse<SkinInfo>> GetCurrentSkinAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/skins/current");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SkinResponse>();
                if (result?.Success == true && result.Skin != null)
                {
                    return ApiResponse<SkinInfo>.Success(new SkinInfo
                    {
                        SkinType = result.Skin.SkinType,
                        DownloadUrl = result.Skin.DownloadUrl,
                        FileHash = result.Skin.FileHash,
                        UploadedAt = result.Skin.UploadedAt
                    });
                }
            }

            return ApiResponse<SkinInfo>.Failure("Скин не найден");
        }
        catch (Exception ex)
        {
            return ApiResponse<SkinInfo>.Failure($"Ошибка: {ex.Message}");
        }
    }

    public async Task<byte[]?> DownloadSkinAsync(int userId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/skins/download/{userId}");

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsByteArrayAsync();
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ApiService] Error downloading skin: {ex.Message}");
            return null;
        }
    }

    public async Task<ApiResponse<bool>> DeleteCurrentSkinAsync()
    {
        try
        {
            var response = await _httpClient.DeleteAsync("/api/skins/current");

            if (response.IsSuccessStatusCode)
            {
                return ApiResponse<bool>.Success(true);
            }

            return ApiResponse<bool>.Failure("Ошибка удаления скина");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.Failure($"Ошибка: {ex.Message}");
        }
    }

    public async Task<ApiResponse<CapeInfo>> UploadCapeAsync(byte[] capeData)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(capeData);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "cape", "cape.png");

            var response = await _httpClient.PostAsync("/api/skins/capes/upload", content);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<CapeUploadResponse>();
                if (result?.Success == true && result.Cape != null)
                {
                    return ApiResponse<CapeInfo>.Success(new CapeInfo
                    {
                        DownloadUrl = result.Cape.DownloadUrl,
                        FileHash = result.Cape.FileHash,
                        UploadedAt = result.Cape.UploadedAt
                    });
                }
                return ApiResponse<CapeInfo>.Failure(result?.Message ?? "Ошибка загрузки плаща");
            }

            var error = await response.Content.ReadFromJsonAsync<CapeUploadResponse>();
            return ApiResponse<CapeInfo>.Failure(error?.Message ?? "Ошибка загрузки плаща");
        }
        catch (Exception ex)
        {
            return ApiResponse<CapeInfo>.Failure($"Ошибка подключения: {ex.Message}");
        }
    }

    public async Task<byte[]?> DownloadCapeAsync(int userId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/skins/capes/download/{userId}");

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsByteArrayAsync();
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ApiService] Error downloading cape: {ex.Message}");
            return null;
        }
    }
}

// DTOs
public class AuthResponseDto
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? Message { get; set; }
    public UserInfoDto? User { get; set; }
    public string? RecoveryCode { get; set; } // Код восстановления
}

public class UserInfoDto
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string MinecraftUUID { get; set; } = "";
    public bool IsWhitelisted { get; set; }
}

public class ProfileResponseDto
{
    public bool Success { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public int PlayTimeMinutes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class ModpackInfoDto
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string SHA256Hash { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string? Changelog { get; set; }
}

public class ServerStatusDto
{
    public bool IsOnline { get; set; }
    public int PlayersOnline { get; set; }
    public int MaxPlayers { get; set; }
    public string ServerVersion { get; set; } = "";
    public string Motd { get; set; } = "";
}

// Response wrapper
public class ApiResponse<T>
{
    public bool IsSuccess { get; set; }
    public T? Data { get; set; }
    public string? ErrorMessage { get; set; }

    public static ApiResponse<T> Success(T data) => new() { IsSuccess = true, Data = data };
    public static ApiResponse<T> Failure(string error) => new() { IsSuccess = false, ErrorMessage = error };
}

// ModpackInfo model
public class ModpackInfo
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string SHA256Hash { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string? Changelog { get; set; }
}

// ProfileInfo model
public class ProfileInfo
{
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public int PlayTimeMinutes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

// ServerStatus model
public class ServerStatus
{
    public bool IsOnline { get; set; }
    public int PlayersOnline { get; set; }
    public int MaxPlayers { get; set; }
    public string ServerVersion { get; set; } = "";
    public string Motd { get; set; } = "";
}

// SkinInfo model
public class SkinInfo
{
    public string SkinType { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string FileHash { get; set; } = "";
    public DateTime UploadedAt { get; set; }
}

// CapeInfo model
public class CapeInfo
{
    public string DownloadUrl { get; set; } = "";
    public string FileHash { get; set; } = "";
    public DateTime UploadedAt { get; set; }
}

// Skin DTOs
public class SkinUploadResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public SkinDto? Skin { get; set; }
}

public class SkinResponse
{
    public bool Success { get; set; }
    public SkinDto? Skin { get; set; }
}

public class SkinDto
{
    public string SkinType { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string FileHash { get; set; } = "";
    public DateTime UploadedAt { get; set; }
}

// Cape DTOs
public class CapeUploadResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public CapeDto? Cape { get; set; }
}

public class CapeDto
{
    public string DownloadUrl { get; set; } = "";
    public string FileHash { get; set; } = "";
    public DateTime UploadedAt { get; set; }
}
