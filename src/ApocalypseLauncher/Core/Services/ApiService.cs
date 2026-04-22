using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Linq;
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
        var allowInsecureTls = string.Equals(
            Environment.GetEnvironmentVariable("LAUNCHER_ALLOW_INSECURE_TLS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        if (allowInsecureTls)
        {
            // Development-only fallback.
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            Console.WriteLine("[ApiService] WARNING: insecure TLS validation is enabled by environment variable.");
        }

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        Console.WriteLine($"[ApiService] Initialized with base URL: {baseUrl}");
    }

    public void SetAuthToken(string token)
    {
        _authToken = token;
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    public string? GetCurrentAuthToken() => _authToken;

    private static async Task<AuthResponseDto?> ReadAuthResponseAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        }
        catch
        {
            return null;
        }
    }

    private static string GetAuthMessage(AuthResponseDto? response, string fallbackMessage)
    {
        return string.IsNullOrWhiteSpace(response?.Message) ? fallbackMessage : response.Message.Trim();
    }

    private static string GetConnectionErrorMessage(Exception ex)
    {
        return ex is TaskCanceledException or TimeoutException
            ? "Сервер отвечает слишком долго. Попробуйте ещё раз."
            : "Не удалось связаться с сервером. Проверьте интернет и попробуйте ещё раз.";
    }

    public async Task<ApiResponse<AuthResult>> RegisterAsync(string username, string password)
    {
        try
        {
            Console.WriteLine($"[ApiService.RegisterAsync] Starting request to {_httpClient.BaseAddress}api/auth/register");
            var request = new { username, password };
            var response = await _httpClient.PostAsJsonAsync("/api/auth/register", request);
            var result = await ReadAuthResponseAsync(response);

            Console.WriteLine($"[ApiService.RegisterAsync] Response status: {response.StatusCode}");
            Console.WriteLine($"[ApiService.RegisterAsync] Response URL: {response.RequestMessage?.RequestUri}");

            if (response.IsSuccessStatusCode && result?.Success == true && !string.IsNullOrWhiteSpace(result.Token))
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
                    RecoveryCode = result.RecoveryCode
                });
            }

            return ApiResponse<AuthResult>.Failure(GetAuthMessage(result, "Ошибка регистрации"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ApiService.RegisterAsync] Exception: {ex.GetType().Name}");
            Console.WriteLine($"[ApiService.RegisterAsync] Message: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"[ApiService.RegisterAsync] Inner: {ex.InnerException.Message}");
            }
            return ApiResponse<AuthResult>.Failure(GetConnectionErrorMessage(ex));
        }
    }

    public async Task<ApiResponse<AuthResult>> LoginAsync(string username, string password)
    {
        try
        {
            var request = new { username, password };
            var response = await _httpClient.PostAsJsonAsync("/api/auth/login", request);
            var result = await ReadAuthResponseAsync(response);

            if (response.IsSuccessStatusCode && result?.Success == true && !string.IsNullOrWhiteSpace(result.Token))
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
                    RequiresPasswordReset = result.RequiresPasswordReset,
                    NotificationMessage = result.NotificationMessage
                });
            }

            return ApiResponse<AuthResult>.Failure(
                GetAuthMessage(result, "Неверное имя пользователя или пароль"),
                result?.RequiresPasswordReset == true,
                result?.NotificationMessage);
        }
        catch (Exception ex)
        {
            return ApiResponse<AuthResult>.Failure(GetConnectionErrorMessage(ex));
        }
    }

    /// <summary>Вход в лаунчер одноразовым кодом, созданным на сайте после авторизации.</summary>
    public async Task<ApiResponse<AuthResult>> RedeemWebHandoffAsync(string handoffCode)
    {
        try
        {
            var trimmed = (handoffCode ?? string.Empty).Trim();
            var response = await _httpClient.PostAsJsonAsync("/api/auth/web-handoff/redeem", new { handoffCode = trimmed });
            var result = await ReadAuthResponseAsync(response);

            if (response.IsSuccessStatusCode && result?.Success == true && !string.IsNullOrWhiteSpace(result.Token))
            {
                SetAuthToken(result.Token);
                return ApiResponse<AuthResult>.Success(new AuthResult
                {
                    Token = result.Token,
                    Username = result.User?.Username ?? string.Empty,
                    Email = result.User?.Email ?? "",
                    MinecraftUUID = result.User?.MinecraftUUID ?? "",
                    UUID = result.User?.MinecraftUUID ?? "",
                    AccessToken = result.Token,
                    IsOffline = false,
                    RequiresPasswordReset = result.RequiresPasswordReset,
                    NotificationMessage = result.NotificationMessage
                });
            }

            return ApiResponse<AuthResult>.Failure(
                GetAuthMessage(result, "Не удалось применить код"),
                result?.RequiresPasswordReset == true,
                result?.NotificationMessage);
        }
        catch (Exception ex)
        {
            return ApiResponse<AuthResult>.Failure(GetConnectionErrorMessage(ex));
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

    public async Task<ApiResponse<string>> ResetPasswordAsync(string username, string recoveryCode, string newPassword)
    {
        try
        {
            var request = new { username, recoveryCode, newPassword };
            var response = await _httpClient.PostAsJsonAsync("/api/auth/reset-password", request);
            var result = await ReadAuthResponseAsync(response);

            if (response.IsSuccessStatusCode && result?.Success == true)
            {
                return ApiResponse<string>.Success(GetAuthMessage(result, "Пароль успешно изменен"));
            }

            return ApiResponse<string>.Failure(GetAuthMessage(result, "Ошибка сброса пароля"));
        }
        catch (Exception ex)
        {
            return ApiResponse<string>.Failure(GetConnectionErrorMessage(ex));
        }
    }

    public async Task<ApiResponse<string>> ResetPasswordByAdminAsync(string username, string resetCode, string newPassword)
    {
        try
        {
            var request = new { username, resetCode, newPassword };
            var response = await _httpClient.PostAsJsonAsync("/api/auth/reset-password-by-admin", request);
            var result = await ReadAuthResponseAsync(response);

            if (response.IsSuccessStatusCode && result?.Success == true)
            {
                return ApiResponse<string>.Success(GetAuthMessage(result, "Пароль успешно изменен"));
            }

            return ApiResponse<string>.Failure(GetAuthMessage(result, "Ошибка смены пароля"));
        }
        catch (Exception ex)
        {
            return ApiResponse<string>.Failure(GetConnectionErrorMessage(ex));
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
                        LastLoginAt = result.LastLoginAt,
                        RequiresPasswordReset = result.RequiresPasswordReset
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

    private HttpRequestMessage CreateAdminRequest(HttpMethod method, string url, string adminKey, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Admin-Key", adminKey);
        if (content != null)
        {
            request.Content = content;
        }

        return request;
    }

    public async Task<ApiResponse<bool>> GetAdminAccessAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/auth/admin/access");
            if (!response.IsSuccessStatusCode)
            {
                return ApiResponse<bool>.Success(false);
            }

            var payload = await response.Content.ReadFromJsonAsync<AdminAccessResponseDto>();
            return ApiResponse<bool>.Success(payload?.IsAdmin == true);
        }
        catch
        {
            return ApiResponse<bool>.Success(false);
        }
    }

    public async Task<ApiResponse<bool>> GetAdminAccessAsync(string adminKey)
    {
        try
        {
            using var request = CreateAdminRequest(HttpMethod.Post, "/api/auth/admin/unlock", adminKey);
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return ApiResponse<bool>.Success(false);
            }

            var payload = await response.Content.ReadFromJsonAsync<AdminAccessResponseDto>();
            return ApiResponse<bool>.Success(payload?.IsAdmin == true);
        }
        catch
        {
            return ApiResponse<bool>.Success(false);
        }
    }

    public async Task<ApiResponse<AdminUserInfo[]>> GetAdminUsersAsync(string adminKey)
    {
        try
        {
            using var request = CreateAdminRequest(HttpMethod.Get, "/api/auth/admin/users", adminKey);
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return ApiResponse<AdminUserInfo[]>.Failure("Нет доступа к списку пользователей");
            }

            var payload = await response.Content.ReadFromJsonAsync<AdminUsersResponseDto>();
            var users = payload?.Users?.Select(u => new AdminUserInfo
            {
                Id = u.Id,
                Username = u.Username,
                IsActive = u.IsActive,
                IsBanned = u.IsBanned,
                IsWhitelisted = u.IsWhitelisted,
                RequiresPasswordReset = u.RequiresPasswordReset,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt
            }).ToArray() ?? Array.Empty<AdminUserInfo>();

            return ApiResponse<AdminUserInfo[]>.Success(users);
        }
        catch (Exception ex)
        {
            return ApiResponse<AdminUserInfo[]>.Failure($"Ошибка: {ex.Message}");
        }
    }

    public async Task<ApiResponse<string>> AdminResetUserPasswordAsync(int userId, string? note, string adminKey)
    {
        try
        {
            using var payload = JsonContent.Create(new { userId, note });
            using var request = CreateAdminRequest(HttpMethod.Post, "/api/auth/admin/reset-password", adminKey, payload);
            var response = await _httpClient.SendAsync(request);
            var result = await ReadAuthResponseAsync(response);
            if (response.IsSuccessStatusCode && result?.Success == true)
            {
                return ApiResponse<string>.Success(result.NotificationMessage ?? result.Message ?? "Принудительная смена пароля включена");
            }

            return ApiResponse<string>.Failure(GetAuthMessage(result, "Не удалось включить принудительную смену пароля"));
        }
        catch (Exception ex)
        {
            return ApiResponse<string>.Failure(GetConnectionErrorMessage(ex));
        }
    }

    public async Task<ApiResponse<string>> AdminSetBanAsync(int userId, bool isBanned, string adminKey, string? banReason = null)
    {
        try
        {
            using var payload = JsonContent.Create(new { userId, isBanned, banReason });
            using var request = CreateAdminRequest(HttpMethod.Post, "/api/auth/admin/set-ban", adminKey, payload);
            var response = await _httpClient.SendAsync(request);
            var result = await ReadAuthResponseAsync(response);
            if (response.IsSuccessStatusCode && result?.Success == true)
            {
                return ApiResponse<string>.Success(result.Message ?? "Операция выполнена");
            }

            return ApiResponse<string>.Failure(GetAuthMessage(result, "Не удалось изменить статус блокировки"));
        }
        catch (Exception ex)
        {
            return ApiResponse<string>.Failure(GetConnectionErrorMessage(ex));
        }
    }

    public async Task<ApiResponse<string>> AdminDeleteUserAsync(int userId, string adminKey)
    {
        try
        {
            using var request = CreateAdminRequest(HttpMethod.Delete, $"/api/auth/admin/users/{userId}", adminKey);
            var response = await _httpClient.SendAsync(request);
            var result = await ReadAuthResponseAsync(response);
            if (response.IsSuccessStatusCode && result?.Success == true)
            {
                return ApiResponse<string>.Success(result.Message ?? "Пользователь удален");
            }

            return ApiResponse<string>.Failure(GetAuthMessage(result, "Не удалось удалить пользователя"));
        }
        catch (Exception ex)
        {
            return ApiResponse<string>.Failure(GetConnectionErrorMessage(ex));
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
    public bool RequiresPasswordReset { get; set; }
    public string? NotificationMessage { get; set; }
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
    public bool RequiresPasswordReset { get; set; }
}

public class AdminAccessResponseDto
{
    public bool Success { get; set; }
    public bool IsAdmin { get; set; }
}

public class AdminUsersResponseDto
{
    public bool Success { get; set; }
    public AdminUserDto[]? Users { get; set; }
}

public class AdminUserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsBanned { get; set; }
    public bool IsWhitelisted { get; set; }
    public bool RequiresPasswordReset { get; set; }
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
    public bool RequiresPasswordReset { get; set; }
    public string? NotificationMessage { get; set; }

    public static ApiResponse<T> Success(T data) => new() { IsSuccess = true, Data = data };
    public static ApiResponse<T> Failure(string error, bool requiresPasswordReset = false, string? notificationMessage = null) =>
        new() { IsSuccess = false, ErrorMessage = error, RequiresPasswordReset = requiresPasswordReset, NotificationMessage = notificationMessage };
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
    public bool RequiresPasswordReset { get; set; }
}

public class AdminUserInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsBanned { get; set; }
    public bool IsWhitelisted { get; set; }
    public bool RequiresPasswordReset { get; set; }
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
