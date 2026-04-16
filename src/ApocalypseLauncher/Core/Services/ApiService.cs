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

    public async Task<ApiResponse<AuthResult>> RegisterAsync(string username, string email, string password)
    {
        try
        {
            Console.WriteLine($"[ApiService.RegisterAsync] Starting request to {_httpClient.BaseAddress}api/auth/register");
            var request = new { username, email, password };
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
                        Email = result.User?.Email ?? email,
                        MinecraftUUID = result.User?.MinecraftUUID ?? "",
                        UUID = result.User?.MinecraftUUID ?? "",
                        AccessToken = result.Token,
                        IsOffline = false
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
}

// DTOs
public class AuthResponseDto
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? Message { get; set; }
    public UserInfoDto? User { get; set; }
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
