using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security;
using System.Text.Json;
using System.Threading.Tasks;
using ApocalypseLauncher.Core.Models;

namespace ApocalypseLauncher.Core.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;
    private string? _authToken;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiService(string baseUrl = "https://localhost:7000")
    {
        // Проверка что используется HTTPS (кроме localhost для разработки)
        var uri = new Uri(baseUrl);
        var isDevelopment = uri.Host == "localhost" || uri.Host == "127.0.0.1";

        if (uri.Scheme != "https" && !isDevelopment)
        {
            throw new SecurityException($"HTTPS required for production. Attempted to connect to: {baseUrl}");
        }

        // Certificate pinning для защиты от MITM
        var certificatePinning = new Security.CertificatePinning(isDevelopment);
        var handler = certificatePinning.CreateSecureHandler();

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };

        Console.WriteLine($"[ApiService] Initialized with base URL: {baseUrl}");
        Console.WriteLine($"[ApiService] Certificate pinning: {(isDevelopment ? "DISABLED (dev)" : "ENABLED")}");
    }

    public void SetAuthToken(string token)
    {
        _authToken = token;
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    public string? GetAuthToken() => _authToken;

    public async Task<ApiResponse<AuthResult>> RegisterAsync(string username, string password)
    {
        try
        {
            // НЕ логируем пароль в production!
            Console.WriteLine($"[ApiService.RegisterAsync] Starting request to {_httpClient.BaseAddress}api/auth/register");
            Console.WriteLine($"[ApiService.RegisterAsync] Username: {username}");
            var request = new { username, password };
            var response = await _httpClient.PostAsJsonAsync("/api/auth/register", request);

            Console.WriteLine($"[ApiService.RegisterAsync] Response status: {response.StatusCode}");
            Console.WriteLine($"[ApiService.RegisterAsync] Response URL: {response.RequestMessage?.RequestUri}");

            if (response.IsSuccessStatusCode)
            {
                var result = await TryReadJsonAsync<AuthResponseDto>(response);
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
                        RecoveryCode = result.RecoveryCode
                    });
                }

                return ApiResponse<AuthResult>.Failure(result?.Message ?? await ReadErrorMessageAsync(response, "РћС€РёР±РєР° СЂРµРіРёСЃС‚СЂР°С†РёРё"));
            }

            return ApiResponse<AuthResult>.Failure(await ReadErrorMessageAsync(response, "РћС€РёР±РєР° СЂРµРіРёСЃС‚СЂР°С†РёРё"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ApiService.RegisterAsync] Exception: {ex.GetType().Name}");
            Console.WriteLine($"[ApiService.RegisterAsync] Message: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"[ApiService.RegisterAsync] Inner: {ex.InnerException.Message}");
            }

            return ApiResponse<AuthResult>.Failure($"РћС€РёР±РєР° РїРѕРґРєР»СЋС‡РµРЅРёСЏ: {ex.Message}");
        }
    }

    public async Task<ApiResponse<AuthResult>> LoginAsync(string username, string password)
    {
        try
        {
            // НЕ логируем пароль!
            Console.WriteLine($"[ApiService.LoginAsync] Login attempt for user: {username}");
            var request = new { username, password };
            var response = await _httpClient.PostAsJsonAsync("/api/auth/login", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await TryReadJsonAsync<AuthResponseDto>(response);
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

                return ApiResponse<AuthResult>.Failure(result?.Message ?? await ReadErrorMessageAsync(response, "РћС€РёР±РєР° РІС…РѕРґР°"));
            }

            return ApiResponse<AuthResult>.Failure(await ReadErrorMessageAsync(response, "РќРµРІРµСЂРЅРѕРµ РёРјСЏ РїРѕР»СЊР·РѕРІР°С‚РµР»СЏ РёР»Рё РїР°СЂРѕР»СЊ"));
        }
        catch (Exception ex)
        {
            return ApiResponse<AuthResult>.Failure($"РћС€РёР±РєР° РїРѕРґРєР»СЋС‡РµРЅРёСЏ: {ex.Message}");
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

            return ApiResponse<bool>.Failure("РўРѕРєРµРЅ РЅРµРґРµР№СЃС‚РІРёС‚РµР»РµРЅ");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.Failure($"РћС€РёР±РєР° РїСЂРѕРІРµСЂРєРё: {ex.Message}");
        }
    }

    public async Task<ApiResponse<ModpackInfo>> GetModpackVersionAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/modpack/version");

            if (response.IsSuccessStatusCode)
            {
                var result = await TryReadJsonAsync<ModpackInfoDto>(response);
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

            return ApiResponse<ModpackInfo>.Failure("РќРµ СѓРґР°Р»РѕСЃСЊ РїРѕР»СѓС‡РёС‚СЊ РёРЅС„РѕСЂРјР°С†РёСЋ Рѕ СЃР±РѕСЂРєРµ");
        }
        catch (Exception ex)
        {
            return ApiResponse<ModpackInfo>.Failure($"РћС€РёР±РєР°: {ex.Message}");
        }
    }

    public async Task<ApiResponse<string>> ResetPasswordAsync(string username, string recoveryCode, string newPassword)
    {
        try
        {
            var request = new { username, recoveryCode, newPassword };
            var response = await _httpClient.PostAsJsonAsync("/api/auth/reset-password", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await TryReadJsonAsync<AuthResponseDto>(response);
                if (result?.Success == true)
                {
                    return ApiResponse<string>.Success(result.Message ?? "РџР°СЂРѕР»СЊ СѓСЃРїРµС€РЅРѕ РёР·РјРµРЅРµРЅ");
                }

                return ApiResponse<string>.Failure(result?.Message ?? await ReadErrorMessageAsync(response, "РћС€РёР±РєР° СЃР±СЂРѕСЃР° РїР°СЂРѕР»СЏ"));
            }

            return ApiResponse<string>.Failure(await ReadErrorMessageAsync(response, "РћС€РёР±РєР° СЃР±СЂРѕСЃР° РїР°СЂРѕР»СЏ"));
        }
        catch (Exception ex)
        {
            return ApiResponse<string>.Failure($"РћС€РёР±РєР° РїРѕРґРєР»СЋС‡РµРЅРёСЏ: {ex.Message}");
        }
    }

    public async Task<ApiResponse<ProfileInfo>> GetProfileAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/auth/profile");

            if (response.IsSuccessStatusCode)
            {
                var result = await TryReadJsonAsync<ProfileResponseDto>(response);
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

            return ApiResponse<ProfileInfo>.Failure("РќРµ СѓРґР°Р»РѕСЃСЊ РїРѕР»СѓС‡РёС‚СЊ РїСЂРѕС„РёР»СЊ");
        }
        catch (Exception ex)
        {
            return ApiResponse<ProfileInfo>.Failure($"РћС€РёР±РєР°: {ex.Message}");
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
                var result = await TryReadJsonAsync<AuthResponseDto>(response);
                if (result?.Success == true)
                {
                    return ApiResponse<string>.Success(result.Message ?? "РќРёРєРЅРµР№Рј РёР·РјРµРЅРµРЅ");
                }

                return ApiResponse<string>.Failure(result?.Message ?? await ReadErrorMessageAsync(response, "РћС€РёР±РєР° СЃРјРµРЅС‹ РЅРёРєРЅРµР№РјР°"));
            }

            return ApiResponse<string>.Failure(await ReadErrorMessageAsync(response, "РћС€РёР±РєР° СЃРјРµРЅС‹ РЅРёРєРЅРµР№РјР°"));
        }
        catch (Exception ex)
        {
            return ApiResponse<string>.Failure($"РћС€РёР±РєР° РїРѕРґРєР»СЋС‡РµРЅРёСЏ: {ex.Message}");
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

            return ApiResponse<bool>.Failure("РќРµ СѓРґР°Р»РѕСЃСЊ РѕР±РЅРѕРІРёС‚СЊ РёРіСЂРѕРІРѕРµ РІСЂРµРјСЏ");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.Failure($"РћС€РёР±РєР°: {ex.Message}");
        }
    }

    public async Task<ApiResponse<ServerStatus>> GetServerStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/server/status");

            if (response.IsSuccessStatusCode)
            {
                var result = await TryReadJsonAsync<ServerStatusDto>(response);
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

            return ApiResponse<ServerStatus>.Failure("РќРµ СѓРґР°Р»РѕСЃСЊ РїРѕР»СѓС‡РёС‚СЊ СЃС‚Р°С‚СѓСЃ СЃРµСЂРІРµСЂР°");
        }
        catch (Exception ex)
        {
            return ApiResponse<ServerStatus>.Failure($"РћС€РёР±РєР°: {ex.Message}");
        }
    }

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
                var result = await TryReadJsonAsync<SkinUploadResponse>(response);
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

                return ApiResponse<SkinInfo>.Failure(result?.Message ?? await ReadErrorMessageAsync(response, "РћС€РёР±РєР° Р·Р°РіСЂСѓР·РєРё СЃРєРёРЅР°"));
            }

            return ApiResponse<SkinInfo>.Failure(await ReadErrorMessageAsync(response, "РћС€РёР±РєР° Р·Р°РіСЂСѓР·РєРё СЃРєРёРЅР°"));
        }
        catch (Exception ex)
        {
            return ApiResponse<SkinInfo>.Failure($"РћС€РёР±РєР° РїРѕРґРєР»СЋС‡РµРЅРёСЏ: {ex.Message}");
        }
    }

    public async Task<ApiResponse<SkinInfo>> GetCurrentSkinAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/skins/current");

            if (response.IsSuccessStatusCode)
            {
                var result = await TryReadJsonAsync<SkinResponse>(response);
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

            return ApiResponse<SkinInfo>.Failure("РЎРєРёРЅ РЅРµ РЅР°Р№РґРµРЅ");
        }
        catch (Exception ex)
        {
            return ApiResponse<SkinInfo>.Failure($"РћС€РёР±РєР°: {ex.Message}");
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

            return ApiResponse<bool>.Failure("РћС€РёР±РєР° СѓРґР°Р»РµРЅРёСЏ СЃРєРёРЅР°");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.Failure($"РћС€РёР±РєР°: {ex.Message}");
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
                var result = await TryReadJsonAsync<CapeUploadResponse>(response);
                if (result?.Success == true && result.Cape != null)
                {
                    return ApiResponse<CapeInfo>.Success(new CapeInfo
                    {
                        DownloadUrl = result.Cape.DownloadUrl,
                        FileHash = result.Cape.FileHash,
                        UploadedAt = result.Cape.UploadedAt
                    });
                }

                return ApiResponse<CapeInfo>.Failure(result?.Message ?? await ReadErrorMessageAsync(response, "РћС€РёР±РєР° Р·Р°РіСЂСѓР·РєРё РїР»Р°С‰Р°"));
            }

            return ApiResponse<CapeInfo>.Failure(await ReadErrorMessageAsync(response, "РћС€РёР±РєР° Р·Р°РіСЂСѓР·РєРё РїР»Р°С‰Р°"));
        }
        catch (Exception ex)
        {
            return ApiResponse<CapeInfo>.Failure($"РћС€РёР±РєР° РїРѕРґРєР»СЋС‡РµРЅРёСЏ: {ex.Message}");
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

    private static async Task<T?> TryReadJsonAsync<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, string fallbackMessage)
    {
        var content = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(content))
        {
            return $"{fallbackMessage}: СЃРµСЂРІРµСЂ РІРµСЂРЅСѓР» РїСѓСЃС‚РѕР№ РѕС‚РІРµС‚ ({(int)response.StatusCode})";
        }

        try
        {
            using var document = JsonDocument.Parse(content);

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var messageElement))
            {
                var message = messageElement.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
        }
        catch (JsonException)
        {
        }

        var shortened = content.Length > 180 ? content[..180] + "..." : content;
        return $"{fallbackMessage}: СЃРµСЂРІРµСЂ РІРµСЂРЅСѓР» РЅРµРІР°Р»РёРґРЅС‹Р№ РѕС‚РІРµС‚ ({(int)response.StatusCode}) - {shortened}";
    }
}

public class AuthResponseDto
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? Message { get; set; }
    public UserInfoDto? User { get; set; }
    public string? RecoveryCode { get; set; }
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

public class ApiResponse<T>
{
    public bool IsSuccess { get; set; }
    public T? Data { get; set; }
    public string? ErrorMessage { get; set; }

    public static ApiResponse<T> Success(T data) => new() { IsSuccess = true, Data = data };
    public static ApiResponse<T> Failure(string error) => new() { IsSuccess = false, ErrorMessage = error };
}

public class ModpackInfo
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string SHA256Hash { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string? Changelog { get; set; }
}

public class ProfileInfo
{
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public int PlayTimeMinutes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class ServerStatus
{
    public bool IsOnline { get; set; }
    public int PlayersOnline { get; set; }
    public int MaxPlayers { get; set; }
    public string ServerVersion { get; set; } = "";
    public string Motd { get; set; } = "";
}

public class SkinInfo
{
    public string SkinType { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string FileHash { get; set; } = "";
    public DateTime UploadedAt { get; set; }
}

public class CapeInfo
{
    public string DownloadUrl { get; set; } = "";
    public string FileHash { get; set; } = "";
    public DateTime UploadedAt { get; set; }
}

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

