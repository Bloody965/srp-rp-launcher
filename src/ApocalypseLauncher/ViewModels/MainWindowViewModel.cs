using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ApocalypseLauncher.Core.Models;
using ApocalypseLauncher.Core.Services;
using ReactiveUI;
using Avalonia.Controls;

namespace ApocalypseLauncher.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private MinecraftInstaller _installer;
    private readonly GameLauncher _gameLauncher;
    private readonly FolderPickerService _folderPicker;
    private ModpackUpdater _modpackUpdater;
    private readonly ApiService _apiService;
    private readonly LauncherUpdateService _updateService;
    private SkinService _skinService;
    private string _minecraftDirectory;
    private readonly HttpClient _httpClient;
    private bool _isLoadingSkinPreview = false;
    private AuthResult? _sessionAuthResult;

    public MainWindowViewModel()
    {
        _folderPicker = new FolderPickerService();
        _minecraftDirectory = _folderPicker.GetDefaultMinecraftDirectory();

        _authService = new AuthService();
        _installer = new MinecraftInstaller(_minecraftDirectory);
        _gameLauncher = new GameLauncher();
        _apiService = new ApiService("https://srp-rp-launcher-production.up.railway.app");
        _modpackUpdater = new ModpackUpdater(_minecraftDirectory, _apiService);
        _updateService = new LauncherUpdateService();
        _skinService = new SkinService(_apiService, _minecraftDirectory);
        _httpClient = new HttpClient();

        // Подписываемся на события
        _installer.StatusChanged += (s, status) => StatusMessage = status;
        _installer.ProgressChanged += (s, progress) => ProgressValue = progress;
        _gameLauncher.OutputReceived += (s, output) =>
        {
            GameOutput += output + "\n";

            // Также добавляем в GameLogs для вкладки логов
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (GameLogs.Contains("появятся здесь") || GameLogs.Contains("не найдены"))
                {
                    GameLogs = output + "\n";
                    LogStatus = "Получение логов в реальном времени...";
                }
                else
                {
                    GameLogs += output + "\n";
                }
            });
        };
        _gameLauncher.GameStarted += (s, e) => IsGameRunning = true;
        _gameLauncher.GameExited += (s, code) =>
        {
            // Вызываем в UI потоке чтобы избежать краша
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsGameRunning = false;
                StatusMessage = $"Игра завершена с кодом: {code}";
                LogStatus = "Игра завершена. Нажмите 'Обновить' для загрузки полных логов.";
            });
        };

        _modpackUpdater.StatusChanged += (s, status) => StatusMessage = status;
        _modpackUpdater.ProgressChanged += (s, progress) => ProgressValue = progress;

        _skinService.StatusChanged += (s, status) => StatusMessage = status;

        // Команды
        LoginCommand = ReactiveCommand.CreateFromTask(LoginAsync);
        RegisterCommand = ReactiveCommand.CreateFromTask(RegisterAsync);
        InstallCommand = ReactiveCommand.CreateFromTask(InstallMinecraftAsync);
        LaunchCommand = ReactiveCommand.CreateFromTask(LaunchGameAsync,
            this.WhenAnyValue(x => x.IsInstalled, x => x.IsGameRunning,
                (installed, running) => installed && !running));
        ChooseFolderCommand = ReactiveCommand.CreateFromTask(ChooseFolderAsync);
        UpdateModpackCommand = ReactiveCommand.CreateFromTask(UpdateModpackAsync);
        UpdateLauncherCommand = ReactiveCommand.CreateFromTask(UpdateLauncherAsync);
        ToggleRegisterCommand = ReactiveCommand.Create(ToggleRegister);
        LogoutCommand = ReactiveCommand.Create(Logout);
        ResetPasswordCommand = ReactiveCommand.Create(ShowResetPassword);
        ConfirmResetPasswordCommand = ReactiveCommand.CreateFromTask(ConfirmResetPasswordAsync);
        CancelResetCommand = ReactiveCommand.Create(CancelReset);
        EditNicknameCommand = ReactiveCommand.Create(StartEditNickname);
        SaveNicknameCommand = ReactiveCommand.CreateFromTask(SaveNicknameAsync);
        CancelEditNicknameCommand = ReactiveCommand.Create(CancelEditNickname);
        UploadSkinCommand = ReactiveCommand.CreateFromTask(UploadSkinAsync);
        UploadCapeCommand = ReactiveCommand.CreateFromTask(UploadCapeAsync);
        DeleteSkinCommand = ReactiveCommand.CreateFromTask(DeleteSkinAsync);
        ShowHomeTabCommand = ReactiveCommand.Create(() => { CurrentTab = "Home"; });
        ShowPersonalizationTabCommand = ReactiveCommand.Create(() => { CurrentTab = "Personalization"; });
        ShowLogsTabCommand = ReactiveCommand.Create(() => { CurrentTab = "Logs"; });
        ShowProfileTabCommand = ReactiveCommand.Create(() => { CurrentTab = "Profile"; });
        ShowAdminTabCommand = ReactiveCommand.CreateFromTask(ShowAdminTabAsync);
        UnlockAdminPanelCommand = ReactiveCommand.CreateFromTask(UnlockAdminPanelAsync);
        RefreshLogsCommand = ReactiveCommand.CreateFromTask(RefreshLogsAsync);
        AnalyzeLogsWithAICommand = ReactiveCommand.CreateFromTask(AnalyzeLogsWithAIAsync);
        CloseLogAnalysisCommand = ReactiveCommand.Create(CloseLogAnalysis);
        RefreshAdminUsersCommand = ReactiveCommand.CreateFromTask(LoadAdminUsersAsync);
        AdminResetPasswordCommand = ReactiveCommand.CreateFromTask(AdminResetPasswordAsync);
        AdminDeleteUserCommand = ReactiveCommand.CreateFromTask(AdminDeleteSelectedUserAsync);
        ToggleBanUserCommand = ReactiveCommand.CreateFromTask(ToggleBanSelectedUserAsync);

        // Загружаем настройки RAM
        LoadRamSettings();

        // Автоматический вход при запуске
        _ = TryAutoLoginAsync();

        // Проверка обновлений лаунчера
        _ = CheckForLauncherUpdatesAsync();
    }

    private string GetTokenFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var launcherDir = Path.Combine(appData, "SRP-RP-Launcher");
        Directory.CreateDirectory(launcherDir);
        return Path.Combine(launcherDir, "session.dat");
    }

    private string GetRamSettingsFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var launcherDir = Path.Combine(appData, "SRP-RP-Launcher");
        Directory.CreateDirectory(launcherDir);
        return Path.Combine(launcherDir, "ram.cfg");
    }

    private void SaveRamSettings()
    {
        try
        {
            File.WriteAllText(GetRamSettingsFilePath(), _allocatedRamGB.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SaveRamSettings] Ошибка: {ex.Message}");
        }
    }

    private void LoadRamSettings()
    {
        try
        {
            var ramFile = GetRamSettingsFilePath();
            if (File.Exists(ramFile))
            {
                var ramText = File.ReadAllText(ramFile);
                if (int.TryParse(ramText, out int ram) && ram >= 2 && ram <= 16)
                {
                    _allocatedRamGB = ram;
                    this.RaisePropertyChanged(nameof(AllocatedRamGB));
                    this.RaisePropertyChanged(nameof(AllocatedRamText));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LoadRamSettings] Ошибка: {ex.Message}");
        }
    }

    private void SaveToken(string token, string username, string email, string? minecraftUuid = null)
    {
        try
        {
            // Format v2: token|username|email|minecraftUuid
            // Format v1 fallback: token|username|email
            var safeUuid = minecraftUuid ?? string.Empty;
            var data = $"{token}|{username}|{email}|{safeUuid}";
            var protectedData = ProtectSessionData(data);
            File.WriteAllText(GetTokenFilePath(), protectedData);
            Console.WriteLine("[SaveToken] Токен сохранен");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SaveToken] Ошибка: {ex.Message}");
        }
    }

    private async Task TryAutoLoginAsync()
    {
        try
        {
            var tokenFile = GetTokenFilePath();
            if (!File.Exists(tokenFile))
            {
                Console.WriteLine("[TryAutoLogin] Токен не найден");
                return;
            }

            var rawSessionData = File.ReadAllText(tokenFile);
            var unprotectedData = UnprotectSessionData(rawSessionData);
            var data = unprotectedData.Split('|');
            if (data.Length < 3)
            {
                Console.WriteLine("[TryAutoLogin] Неверный формат токена");
                return;
            }

            var token = data[0];
            var username = data[1];
            var email = data[2];
            var minecraftUuid = data.Length >= 4 ? data[3] : string.Empty;

            _apiService.SetAuthToken(token);
            var verifyResult = await _apiService.VerifyTokenAsync();

            if (verifyResult.IsSuccess)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLoggedIn = true;
                    CurrentView = "Main";
                    Username = username;
                    UserEmail = email;
                    StatusMessage = $"Добро пожаловать, {username}!";
                });

                // Подготавливаем auth-данные для запуска игры.
                // Даже если UUID не был сохранён (старый формат session.dat), offline-UUID алгоритм совпадает с серверным.
                if (string.IsNullOrWhiteSpace(minecraftUuid))
                {
                    minecraftUuid = _authService.AuthenticateOffline(username).UUID;
                }

                _sessionAuthResult = new AuthResult
                {
                    Token = token,
                    Username = username,
                    Email = email,
                    MinecraftUUID = minecraftUuid,
                    UUID = minecraftUuid,
                    AccessToken = token,
                    IsOffline = false,
                    CreatedAt = DateTime.Now
                };

                CheckInstallation();
                await CheckModpackVersionAsync();
                await LoadProfileAsync();
                await LoadCurrentSkinAsync();
                await RefreshAdminAccessAsync();
                Console.WriteLine("[TryAutoLogin] Автоматический вход выполнен");
            }
            else
            {
                File.Delete(tokenFile);
                Console.WriteLine("[TryAutoLogin] Токен недействителен, удален");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TryAutoLogin] Ошибка: {ex.Message}");
        }
    }

    private static string ProtectSessionData(string plainText)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var bytes = Encoding.UTF8.GetBytes(plainText);
                var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return "enc:" + Convert.ToBase64String(protectedBytes);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProtectSessionData] Warning: {ex.Message}");
        }

        // Fallback for non-Windows/runtime constraints.
        return plainText;
    }

    private static string UnprotectSessionData(string storedData)
    {
        try
        {
            if (storedData.StartsWith("enc:", StringComparison.Ordinal))
            {
                if (!OperatingSystem.IsWindows())
                {
                    return string.Empty;
                }

                var payload = storedData.Substring(4);
                var protectedBytes = Convert.FromBase64String(payload);
                var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UnprotectSessionData] Warning: {ex.Message}");
            return string.Empty;
        }

        // Backward compatibility for old plaintext session.dat.
        return storedData;
    }

    private string _username = "Survivor";
    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    private string _email = "";
    public string Email
    {
        get => _email;
        set => this.RaiseAndSetIfChanged(ref _email, value);
    }

    private string _userEmail = "";
    public string UserEmail
    {
        get => _userEmail;
        set => this.RaiseAndSetIfChanged(ref _userEmail, value);
    }

    private string _playTimeFormatted = "0 ч";
    public string PlayTimeFormatted
    {
        get => _playTimeFormatted;
        set => this.RaiseAndSetIfChanged(ref _playTimeFormatted, value);
    }

    private int _allocatedRamGB = 4;
    public int AllocatedRamGB
    {
        get => _allocatedRamGB;
        set
        {
            this.RaiseAndSetIfChanged(ref _allocatedRamGB, value);
            this.RaisePropertyChanged(nameof(AllocatedRamText));
            SaveRamSettings();
        }
    }

    public string AllocatedRamText => $"{_allocatedRamGB} GB";

    private bool _isServerOnline = false;
    public bool IsServerOnline
    {
        get => _isServerOnline;
        set
        {
            this.RaiseAndSetIfChanged(ref _isServerOnline, value);
            this.RaisePropertyChanged(nameof(ServerStatusText));
            this.RaisePropertyChanged(nameof(ServerStatusColor));
        }
    }

    private int _playersOnline = 0;
    public int PlayersOnline
    {
        get => _playersOnline;
        set
        {
            this.RaiseAndSetIfChanged(ref _playersOnline, value);
            this.RaisePropertyChanged(nameof(ServerStatusText));
        }
    }

    private int _maxPlayers = 100;
    public int MaxPlayers
    {
        get => _maxPlayers;
        set => this.RaiseAndSetIfChanged(ref _maxPlayers, value);
    }

    public string ServerStatusText => IsServerOnline
        ? $"🟢 Онлайн • {PlayersOnline}/{MaxPlayers} игроков"
        : "🔴 Офлайн";

    public string ServerStatusColor => IsServerOnline ? "#53dc96" : "#ff6a4a";

    private string _aboutProjectText = "Информация о проекте будет добавлена позже.";
    public string AboutProjectText
    {
        get => _aboutProjectText;
        set => this.RaiseAndSetIfChanged(ref _aboutProjectText, value);
    }

    private int _playTimeMinutes = 0;
    public int PlayTimeMinutes
    {
        get => _playTimeMinutes;
        set
        {
            this.RaiseAndSetIfChanged(ref _playTimeMinutes, value);
            UpdatePlayTimeFormatted();
        }
    }

    private void UpdatePlayTimeFormatted()
    {
        var hours = _playTimeMinutes / 60;
        var minutes = _playTimeMinutes % 60;
        PlayTimeFormatted = hours > 0 ? $"{hours} ч {minutes} мин" : $"{minutes} мин";
    }

    private string _newNickname = "";
    public string NewNickname
    {
        get => _newNickname;
        set => this.RaiseAndSetIfChanged(ref _newNickname, value);
    }

    private bool _isEditingNickname = false;
    public bool IsEditingNickname
    {
        get => _isEditingNickname;
        set => this.RaiseAndSetIfChanged(ref _isEditingNickname, value);
    }

    private string _password = "";
    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }

    private string _recoveryCode = "";
    public string RecoveryCode
    {
        get => _recoveryCode;
        set => this.RaiseAndSetIfChanged(ref _recoveryCode, value);
    }

    private string _recoveryCodeDisplay = "";
    public string RecoveryCodeDisplay
    {
        get => _recoveryCodeDisplay;
        set => this.RaiseAndSetIfChanged(ref _recoveryCodeDisplay, value);
    }

    private bool _showRecoveryCode = false;
    public bool ShowRecoveryCode
    {
        get => _showRecoveryCode;
        set => this.RaiseAndSetIfChanged(ref _showRecoveryCode, value);
    }

    private string _newPassword = "";
    public string NewPassword
    {
        get => _newPassword;
        set => this.RaiseAndSetIfChanged(ref _newPassword, value);
    }

    private string? _loginErrorMessage;
    public string? LoginErrorMessage
    {
        get => _loginErrorMessage;
        set
        {
            this.RaiseAndSetIfChanged(ref _loginErrorMessage, value);
            this.RaisePropertyChanged(nameof(HasLoginError));
        }
    }

    public bool HasLoginError => !string.IsNullOrEmpty(LoginErrorMessage);

    private bool _isRegistering = false;
    public bool IsRegistering
    {
        get => _isRegistering;
        set => this.RaiseAndSetIfChanged(ref _isRegistering, value);
    }

    private bool _isResettingPassword = false;
    public bool IsResettingPassword
    {
        get => _isResettingPassword;
        set => this.RaiseAndSetIfChanged(ref _isResettingPassword, value);
    }

    private bool _isForcedPasswordReset;
    public bool IsForcedPasswordReset
    {
        get => _isForcedPasswordReset;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _isForcedPasswordReset, value))
            {
                this.RaisePropertyChanged(nameof(ResetPasswordHelpText));
            }
        }
    }

    private string _forcedPasswordResetMessage = string.Empty;
    public string ForcedPasswordResetMessage
    {
        get => _forcedPasswordResetMessage;
        set => this.RaiseAndSetIfChanged(ref _forcedPasswordResetMessage, value);
    }

    private bool _isLoggingIn;
    public bool IsLoggingIn
    {
        get => _isLoggingIn;
        set
        {
            this.RaiseAndSetIfChanged(ref _isLoggingIn, value);
            this.RaisePropertyChanged(nameof(IsAuthBusy));
            this.RaisePropertyChanged(nameof(LoginButtonText));
        }
    }

    private bool _isRegisteringAccount;
    public bool IsRegisteringAccount
    {
        get => _isRegisteringAccount;
        set
        {
            this.RaiseAndSetIfChanged(ref _isRegisteringAccount, value);
            this.RaisePropertyChanged(nameof(IsAuthBusy));
            this.RaisePropertyChanged(nameof(RegisterButtonText));
        }
    }

    private bool _isResettingPasswordRequest;
    public bool IsResettingPasswordRequest
    {
        get => _isResettingPasswordRequest;
        set
        {
            this.RaiseAndSetIfChanged(ref _isResettingPasswordRequest, value);
            this.RaisePropertyChanged(nameof(IsAuthBusy));
            this.RaisePropertyChanged(nameof(ResetPasswordButtonText));
        }
    }

    public bool IsAuthBusy => IsLoggingIn || IsRegisteringAccount || IsResettingPasswordRequest;
    public string LoginButtonText => IsLoggingIn ? "ВХОД..." : "ВОЙТИ В УЗЕЛ";
    public string RegisterButtonText => IsRegisteringAccount ? "СОЗДАНИЕ..." : "СОЗДАТЬ АККАУНТ";
    public string ResetPasswordButtonText => IsResettingPasswordRequest ? "СОХРАНЕНИЕ..." : "СОХРАНИТЬ НОВЫЙ ПАРОЛЬ";

    private bool _hasLauncherUpdate;
    public bool HasLauncherUpdate
    {
        get => _hasLauncherUpdate;
        set => this.RaiseAndSetIfChanged(ref _hasLauncherUpdate, value);
    }

    private string _latestLauncherVersion = "";
    public string LatestLauncherVersion
    {
        get => _latestLauncherVersion;
        set => this.RaiseAndSetIfChanged(ref _latestLauncherVersion, value);
    }

    private string _launcherUpdateUrl = "";

    private bool _isFullscreen;
    public bool IsFullscreen
    {
        get => _isFullscreen;
        set => this.RaiseAndSetIfChanged(ref _isFullscreen, value);
    }

    private bool _isClassicSkin = true;
    public bool IsClassicSkin
    {
        get => _isClassicSkin;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _isClassicSkin, value))
            {
                return;
            }

            if (value == _isSlimSkin)
            {
                _isSlimSkin = !value;
                this.RaisePropertyChanged(nameof(IsSlimSkin));
            }
        }
    }

    private bool _isSlimSkin = false;
    public bool IsSlimSkin
    {
        get => _isSlimSkin;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _isSlimSkin, value))
            {
                return;
            }

            if (value == _isClassicSkin)
            {
                _isClassicSkin = !value;
                this.RaisePropertyChanged(nameof(IsClassicSkin));
            }
        }
    }

    private string _skinStatus = "Скин не загружен";
    public string SkinStatus
    {
        get => _skinStatus;
        set => this.RaiseAndSetIfChanged(ref _skinStatus, value);
    }

    private Avalonia.Media.Imaging.Bitmap? _currentSkinPreview;
    public Avalonia.Media.Imaging.Bitmap? CurrentSkinPreview
    {
        get => _currentSkinPreview;
        set => this.RaiseAndSetIfChanged(ref _currentSkinPreview, value);
    }

    private string? _currentSkinPath;
    public string? CurrentSkinPath
    {
        get => _currentSkinPath;
        set => this.RaiseAndSetIfChanged(ref _currentSkinPath, value);
    }

    private bool _isLoggedIn;
    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        set => this.RaiseAndSetIfChanged(ref _isLoggedIn, value);
    }

    private bool _isInstalled;
    public bool IsInstalled
    {
        get => _isInstalled;
        set => this.RaiseAndSetIfChanged(ref _isInstalled, value);
    }

    private bool _isGameRunning;
    public bool IsGameRunning
    {
        get => _isGameRunning;
        set => this.RaiseAndSetIfChanged(ref _isGameRunning, value);
    }

    private string _statusMessage = "Добро пожаловать в постапокалипсис...";
    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    private int _progressValue;
    public int ProgressValue
    {
        get => _progressValue;
        set => this.RaiseAndSetIfChanged(ref _progressValue, value);
    }

    private string _gameOutput = string.Empty;
    public string GameOutput
    {
        get => _gameOutput;
        set => this.RaiseAndSetIfChanged(ref _gameOutput, value);
    }

    private string _modpackVersion = "Проверка...";
    public string ModpackVersion
    {
        get => _modpackVersion;
        set => this.RaiseAndSetIfChanged(ref _modpackVersion, value);
    }

    private string _currentView = "Login";
    public string CurrentView
    {
        get => _currentView;
        set => this.RaiseAndSetIfChanged(ref _currentView, value);
    }

    private string _currentTab = "Home";
    public string CurrentTab
    {
        get => _currentTab;
        set => this.RaiseAndSetIfChanged(ref _currentTab, value);
    }

    private bool _isAdmin;
    public bool IsAdmin
    {
        get => _isAdmin;
        set => this.RaiseAndSetIfChanged(ref _isAdmin, value);
    }

    private bool _isAdminBusy;
    public bool IsAdminBusy
    {
        get => _isAdminBusy;
        set => this.RaiseAndSetIfChanged(ref _isAdminBusy, value);
    }

    private string _adminStatusMessage = "Проверка прав администратора...";
    public string AdminStatusMessage
    {
        get => _adminStatusMessage;
        set => this.RaiseAndSetIfChanged(ref _adminStatusMessage, value);
    }

    private AdminUserItem? _selectedAdminUser;
    public AdminUserItem? SelectedAdminUser
    {
        get => _selectedAdminUser;
        set => this.RaiseAndSetIfChanged(ref _selectedAdminUser, value);
    }

    private string _adminNewPassword = string.Empty;
    public string AdminNewPassword
    {
        get => _adminNewPassword;
        set => this.RaiseAndSetIfChanged(ref _adminNewPassword, value);
    }

    private string _adminBanReason = string.Empty;
    public string AdminBanReason
    {
        get => _adminBanReason;
        set => this.RaiseAndSetIfChanged(ref _adminBanReason, value);
    }

    public ObservableCollection<AdminUserItem> AdminUsers { get; } = new();

    private string _adminSecurityKey = string.Empty;
    public string AdminSecurityKey
    {
        get => _adminSecurityKey;
        set => this.RaiseAndSetIfChanged(ref _adminSecurityKey, value);
    }

    private bool _isAdminPanelUnlocked;
    public bool IsAdminPanelUnlocked
    {
        get => _isAdminPanelUnlocked;
        set => this.RaiseAndSetIfChanged(ref _isAdminPanelUnlocked, value);
    }

    private string _gameLogs = "Логи игры появятся здесь после запуска Minecraft...";
    public string GameLogs
    {
        get => _gameLogs;
        set => this.RaiseAndSetIfChanged(ref _gameLogs, value);
    }

    private string _logStatus = "Ожидание запуска игры";
    public string LogStatus
    {
        get => _logStatus;
        set => this.RaiseAndSetIfChanged(ref _logStatus, value);
    }

    private bool _isAnalyzingLogs = false;
    public bool IsAnalyzingLogs
    {
        get => _isAnalyzingLogs;
        set => this.RaiseAndSetIfChanged(ref _isAnalyzingLogs, value);
    }

    private string _logAnalysisResult = string.Empty;
    public string LogAnalysisResult
    {
        get => _logAnalysisResult;
        set => this.RaiseAndSetIfChanged(ref _logAnalysisResult, value);
    }

    private bool _isLogAnalysisVisible;
    public bool IsLogAnalysisVisible
    {
        get => _isLogAnalysisVisible;
        set => this.RaiseAndSetIfChanged(ref _isLogAnalysisVisible, value);
    }

    public ReactiveCommand<Unit, Unit> ShowHomeTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowPersonalizationTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowLogsTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowProfileTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowAdminTabCommand { get; }
    public ReactiveCommand<Unit, Unit> UnlockAdminPanelCommand { get; }

    public ReactiveCommand<Unit, Unit> LoginCommand { get; }
    public ReactiveCommand<Unit, Unit> RegisterCommand { get; }
    public ReactiveCommand<Unit, Unit> InstallCommand { get; }
    public ReactiveCommand<Unit, Unit> LaunchCommand { get; }
    public ReactiveCommand<Unit, Unit> ChooseFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> UpdateModpackCommand { get; }
    public ReactiveCommand<Unit, Unit> UpdateLauncherCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleRegisterCommand { get; }
    public ReactiveCommand<Unit, Unit> LogoutCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetPasswordCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfirmResetPasswordCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelResetCommand { get; }
    public ReactiveCommand<Unit, Unit> EditNicknameCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveNicknameCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelEditNicknameCommand { get; }
    public ReactiveCommand<Unit, Unit> UploadSkinCommand { get; }
    public ReactiveCommand<Unit, Unit> UploadCapeCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteSkinCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshLogsCommand { get; }
    public ReactiveCommand<Unit, Unit> AnalyzeLogsWithAICommand { get; }
    public ReactiveCommand<Unit, Unit> CloseLogAnalysisCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshAdminUsersCommand { get; }
    public ReactiveCommand<Unit, Unit> AdminResetPasswordCommand { get; }
    public ReactiveCommand<Unit, Unit> AdminDeleteUserCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleBanUserCommand { get; }

    private async Task ChooseFolderAsync()
    {
        // This needs to be called from the View with the Window reference
        // For now, we'll just show a message
        StatusMessage = "Используйте кнопку 'Выбрать папку' в интерфейсе";
    }

    public async Task ChooseFolderFromWindowAsync(Window window)
    {
        var folder = await _folderPicker.PickFolderAsync(window, "Выберите папку для установки Minecraft");

        if (!string.IsNullOrEmpty(folder))
        {
            _minecraftDirectory = folder;
            StatusMessage = $"Папка установки: {folder}";

            // Пересоздаем installer с новой папкой
            _installer = new MinecraftInstaller(_minecraftDirectory);

            // Подписываемся на события заново
            _installer.StatusChanged += (s, status) => StatusMessage = status;
            _installer.ProgressChanged += (s, progress) => ProgressValue = progress;

            // Обновляем ModpackUpdater с новой папкой
            _modpackUpdater = new ModpackUpdater(_minecraftDirectory, _apiService);
            _modpackUpdater.StatusChanged += (s, status) => StatusMessage = status;
            _modpackUpdater.ProgressChanged += (s, progress) => ProgressValue = progress;

            CheckInstallation();
        }
    }

    private async Task ShowAdminTabAsync()
    {
        if (!IsAdmin)
        {
            AdminStatusMessage = "Нет прав для доступа к админ-панели";
            return;
        }

        CurrentTab = "Admin";
        if (IsAdminPanelUnlocked)
        {
            await LoadAdminUsersAsync();
        }
        else
        {
            AdminStatusMessage = "Введите Admin Security Key, чтобы разблокировать админ-панель";
        }
    }

    private async Task UnlockAdminPanelAsync()
    {
        if (!IsAdmin)
        {
            AdminStatusMessage = "Нет прав администратора";
            return;
        }

        if (string.IsNullOrWhiteSpace(AdminSecurityKey))
        {
            AdminStatusMessage = "Введите Admin Security Key";
            return;
        }

        IsAdminBusy = true;
        try
        {
            var access = await _apiService.GetAdminAccessAsync(AdminSecurityKey.Trim());
            IsAdminPanelUnlocked = access.Data == true;
            if (!IsAdminPanelUnlocked)
            {
                AdminStatusMessage = "Неверный Admin Security Key";
                return;
            }

            AdminStatusMessage = "Админ-панель разблокирована";
            await LoadAdminUsersAsync();
        }
        finally
        {
            IsAdminBusy = false;
        }
    }

    private async Task RefreshAdminAccessAsync()
    {
        try
        {
            var access = await _apiService.GetAdminAccessAsync();
            IsAdmin = access.Data == true;
            AdminStatusMessage = IsAdmin ? "Админ-доступ подтвержден" : "У вас нет прав администратора";
            IsAdminPanelUnlocked = false;
            AdminSecurityKey = string.Empty;
            if (!IsAdmin && CurrentTab == "Admin")
            {
                CurrentTab = "Home";
            }
        }
        catch (Exception ex)
        {
            IsAdmin = false;
            AdminStatusMessage = $"Ошибка проверки админ-доступа: {ex.Message}";
        }
    }

    private async Task LoadAdminUsersAsync()
    {
        if (!IsAdmin)
        {
            return;
        }

        if (!IsAdminPanelUnlocked || string.IsNullOrWhiteSpace(AdminSecurityKey))
        {
            AdminStatusMessage = "Сначала разблокируйте админ-панель через Admin Security Key";
            return;
        }

        try
        {
            IsAdminBusy = true;
            AdminStatusMessage = "Загрузка игроков...";
            var result = await _apiService.GetAdminUsersAsync(AdminSecurityKey.Trim());
            if (!result.IsSuccess || result.Data == null)
            {
                AdminStatusMessage = result.ErrorMessage ?? "Не удалось загрузить список игроков";
                return;
            }

            AdminUsers.Clear();
            foreach (var user in result.Data)
            {
                AdminUsers.Add(new AdminUserItem
                {
                    Id = user.Id,
                    Username = user.Username,
                    IsActive = user.IsActive,
                    IsBanned = user.IsBanned,
                    IsWhitelisted = user.IsWhitelisted,
                    RequiresPasswordReset = user.RequiresPasswordReset,
                    CreatedAt = user.CreatedAt,
                    LastLoginAt = user.LastLoginAt
                });
            }

            SelectedAdminUser = AdminUsers.FirstOrDefault();
            AdminStatusMessage = $"Загружено пользователей: {AdminUsers.Count}";
        }
        catch (Exception ex)
        {
            AdminStatusMessage = $"Ошибка загрузки игроков: {ex.Message}";
        }
        finally
        {
            IsAdminBusy = false;
        }
    }

    private async Task AdminResetPasswordAsync()
    {
        if (SelectedAdminUser == null)
        {
            AdminStatusMessage = "Выберите пользователя";
            return;
        }

        try
        {
            IsAdminBusy = true;
            var note = string.IsNullOrWhiteSpace(AdminNewPassword) ? null : AdminNewPassword.Trim();
            var result = await _apiService.AdminResetUserPasswordAsync(SelectedAdminUser.Id, note, AdminSecurityKey.Trim());
            AdminStatusMessage = result.IsSuccess
                ? (result.Data ?? "Игроку включена принудительная смена пароля")
                : (result.ErrorMessage ?? "Ошибка включения принудительной смены пароля");
            AdminNewPassword = string.Empty;
            await LoadAdminUsersAsync();
        }
        finally
        {
            IsAdminBusy = false;
        }
    }

    private async Task ToggleBanSelectedUserAsync()
    {
        if (SelectedAdminUser == null)
        {
            AdminStatusMessage = "Выберите пользователя";
            return;
        }

        try
        {
            IsAdminBusy = true;
            var targetBanState = !SelectedAdminUser.IsBanned;
            var reason = targetBanState ? AdminBanReason?.Trim() : null;
            var result = await _apiService.AdminSetBanAsync(SelectedAdminUser.Id, targetBanState, AdminSecurityKey.Trim(), reason);
            AdminStatusMessage = result.IsSuccess ? (result.Data ?? "Статус блокировки обновлен") : (result.ErrorMessage ?? "Ошибка обновления блокировки");

            if (result.IsSuccess)
            {
                SelectedAdminUser.IsBanned = targetBanState;
                this.RaisePropertyChanged(nameof(SelectedAdminUser));
                if (targetBanState)
                {
                    AdminBanReason = string.Empty;
                }
            }
        }
        finally
        {
            IsAdminBusy = false;
        }
    }

    private async Task AdminDeleteSelectedUserAsync()
    {
        if (SelectedAdminUser == null)
        {
            AdminStatusMessage = "Выберите пользователя";
            return;
        }

        if (SelectedAdminUser.Username.Equals(Username, StringComparison.OrdinalIgnoreCase))
        {
            AdminStatusMessage = "Нельзя удалить текущий аккаунт администратора";
            return;
        }

        try
        {
            IsAdminBusy = true;
            var result = await _apiService.AdminDeleteUserAsync(SelectedAdminUser.Id, AdminSecurityKey.Trim());
            AdminStatusMessage = result.IsSuccess ? (result.Data ?? "Пользователь удален") : (result.ErrorMessage ?? "Ошибка удаления пользователя");
            if (result.IsSuccess)
            {
                await LoadAdminUsersAsync();
            }
        }
        finally
        {
            IsAdminBusy = false;
        }
    }

    private void Logout()
    {
        IsLoggedIn = false;
        IsRegistering = false;
        CurrentView = "Login";
        Password = "";
        LoginErrorMessage = null;
        CurrentSkinPath = null;
        CurrentSkinPreview = null;
        IsForcedPasswordReset = false;
        ForcedPasswordResetMessage = string.Empty;
        IsAdmin = false;
        IsAdminPanelUnlocked = false;
        AdminSecurityKey = string.Empty;
        IsAdminBusy = false;
        CurrentTab = "Home";
        AdminUsers.Clear();
        SelectedAdminUser = null;
        AdminNewPassword = string.Empty;
        AdminBanReason = string.Empty;
        StatusMessage = "Вы вышли из аккаунта";

        // Удаляем сохраненный токен
        try
        {
            var tokenFile = GetTokenFilePath();
            if (File.Exists(tokenFile))
            {
                File.Delete(tokenFile);
                Console.WriteLine("[Logout] Токен удален");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Logout] Ошибка удаления токена: {ex.Message}");
        }

        Console.WriteLine("[Logout] Пользователь вышел из системы");
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    private static bool IsTimeoutException(Exception ex) => ex is TaskCanceledException or TimeoutException;

    private static string NormalizeUsername(string? username) => username?.Trim() ?? string.Empty;

    private static string NormalizeRecoveryCode(string? recoveryCode) => recoveryCode?.Trim().ToUpperInvariant() ?? string.Empty;

    private static string BuildRecoveryCodeWarning(string recoveryCode)
    {
        return $"✅ РЕГИСТРАЦИЯ УСПЕШНА!\n\n⚠️ СОХРАНИТЕ КОД ВОССТАНОВЛЕНИЯ НИЖЕ.\nОн нужен для сброса пароля и больше не будет показан.\nМы не отправляем его на почту.\nСкопируйте код прямо сейчас (Ctrl+C).\n\nКОД: {recoveryCode}";
    }

    private string GetConnectionErrorMessage(Exception ex)
    {
        return IsTimeoutException(ex)
            ? "Сервер отвечает слишком долго. Попробуйте ещё раз."
            : "Не удалось связаться с сервером. Проверьте интернет и попробуйте ещё раз.";
    }

    private void ClearAuthError()
    {
        LoginErrorMessage = null;
    }

    private void SetAuthError(string message, string statusMessage)
    {
        LoginErrorMessage = message;
        StatusMessage = statusMessage;
    }

    private void ClearRecoveryCodeBanner()
    {
        ShowRecoveryCode = false;
        RecoveryCodeDisplay = string.Empty;
    }

    private void ShowRecoveryCodeBanner(string recoveryCode)
    {
        RecoveryCodeDisplay = recoveryCode;
        ShowRecoveryCode = true;
    }

    private void StartLoginRequest()
    {
        IsLoggingIn = true;
        ClearAuthError();
        ClearRecoveryCodeBanner();
        StatusMessage = "Входим в аккаунт...";
    }

    private void FinishLoginRequest()
    {
        IsLoggingIn = false;
    }

    private void StartRegisterRequest()
    {
        IsRegisteringAccount = true;
        ClearAuthError();
        StatusMessage = "Создаём аккаунт...";
    }

    private void FinishRegisterRequest()
    {
        IsRegisteringAccount = false;
    }

    private void StartResetPasswordRequest()
    {
        IsResettingPasswordRequest = true;
        ClearAuthError();
        StatusMessage = "Сохраняем новый пароль...";
    }

    private void FinishResetPasswordRequest()
    {
        IsResettingPasswordRequest = false;
    }

    private void UpdateAuthBusyState(bool login = false, bool register = false, bool reset = false)
    {
        IsLoggingIn = login;
        IsRegisteringAccount = register;
        IsResettingPasswordRequest = reset;
        NotifyAuthUiStateChanged();
    }

    private void ResetAuthBusyState()
    {
        UpdateAuthBusyState();
    }

    private void NotifyAuthUiStateChanged()
    {
        this.RaisePropertyChanged(nameof(IsAuthBusy));
        this.RaisePropertyChanged(nameof(LoginButtonText));
        this.RaisePropertyChanged(nameof(RegisterButtonText));
        this.RaisePropertyChanged(nameof(ResetPasswordButtonText));
        this.RaisePropertyChanged(nameof(BusyHintText));
        this.RaisePropertyChanged(nameof(LoginHelpText));
        this.RaisePropertyChanged(nameof(RegistrationHelpText));
        this.RaisePropertyChanged(nameof(ResetPasswordHelpText));
    }

    public string BusyHintText => IsLoggingIn
        ? "Выполняем вход..."
        : IsRegisteringAccount
            ? "Создаём аккаунт..."
            : IsResettingPasswordRequest
                ? "Сохраняем новый пароль..."
                : string.Empty;

    public string ResetPasswordHelpText => IsForcedPasswordReset
        ? "Администратор запросил смену пароля. Введите одноразовый код сброса и задайте новый пароль."
        : "Используйте имя пользователя и код восстановления, который вы получили при регистрации. Код не отправляется на почту и не показывается повторно.";
    public string RegistrationHelpText => "После регистрации мы один раз покажем код восстановления. Сохраните его сразу — он нужен для сброса пароля.";
    public string LoginHelpText => "Авторизуйтесь, чтобы открыть терминал запуска. Если аккаунта ещё нет, его можно зарегистрировать прямо здесь.";

    private bool ValidateLoginInputs()
    {
        Username = NormalizeUsername(Username);

        if (IsBlank(Username))
        {
            SetAuthError("Введите имя пользователя!", "Проверьте введённые данные");
            return false;
        }

        if (IsBlank(Password))
        {
            SetAuthError("Введите пароль!", "Проверьте введённые данные");
            return false;
        }

        return true;
    }

    private bool ValidateRegisterInputs()
    {
        Username = NormalizeUsername(Username);

        if (IsBlank(Username))
        {
            SetAuthError("Введите имя пользователя!", "Проверьте введённые данные");
            return false;
        }

        if (IsBlank(Password))
        {
            SetAuthError("Введите пароль!", "Проверьте введённые данные");
            return false;
        }

        return true;
    }

    private bool ValidateResetPasswordInputs()
    {
        Username = NormalizeUsername(Username);
        if (!IsForcedPasswordReset)
        {
            RecoveryCode = NormalizeRecoveryCode(RecoveryCode);
        }

        if (IsBlank(Username))
        {
            SetAuthError("Введите имя пользователя!", "Проверьте введённые данные");
            return false;
        }

        if (!IsForcedPasswordReset && IsBlank(RecoveryCode))
        {
            SetAuthError("Введите код восстановления!", "Проверьте введённые данные");
            return false;
        }

        if (!IsForcedPasswordReset && RecoveryCode.Length < 16)
        {
            SetAuthError("Код восстановления должен содержать 16 символов.", "Проверьте введённые данные");
            return false;
        }

        if (IsForcedPasswordReset && IsBlank(RecoveryCode))
        {
            SetAuthError("Введите одноразовый код сброса от администратора!", "Проверьте введённые данные");
            return false;
        }

        if (IsBlank(NewPassword))
        {
            SetAuthError("Введите новый пароль!", "Проверьте введённые данные");
            return false;
        }

        return true;
    }

    private void ShowResetPassword()
    {
        if (IsAuthBusy)
        {
            return;
        }

        IsResettingPassword = true;
        IsForcedPasswordReset = false;
        ForcedPasswordResetMessage = string.Empty;
        IsRegistering = false;
        RecoveryCode = string.Empty;
        NewPassword = string.Empty;
        ClearAuthError();
        StatusMessage = "Введите имя пользователя, код восстановления и новый пароль";
        Console.WriteLine("[ShowResetPassword] Открыт экран сброса пароля");
    }

    private void CancelReset()
    {
        if (IsAuthBusy)
        {
            return;
        }

        IsResettingPassword = false;
        IsForcedPasswordReset = false;
        ForcedPasswordResetMessage = string.Empty;
        RecoveryCode = string.Empty;
        NewPassword = string.Empty;
        ClearAuthError();
        StatusMessage = "Вход в аккаунт";
        Console.WriteLine("[CancelReset] Отмена сброса пароля");
    }

    private void ToggleRegister()
    {
        if (IsAuthBusy || IsResettingPassword)
        {
            return;
        }

        IsRegistering = !IsRegistering;
        ClearAuthError();
        StatusMessage = IsRegistering ? "Регистрация нового аккаунта" : "Вход в аккаунт";
    }

    private void PrepareForSuccessfulRegistration(string recoveryCode)
    {
        IsLoggedIn = false;
        IsRegistering = false;
        IsResettingPassword = false;
        CurrentView = "Login";
        Password = string.Empty;
        RecoveryCode = string.Empty;
        NewPassword = string.Empty;
        ShowRecoveryCodeBanner(recoveryCode);
        LoginErrorMessage = BuildRecoveryCodeWarning(recoveryCode);
        StatusMessage = "Регистрация завершена. Сохраните код восстановления и войдите.";
    }

    private void PrepareForSuccessfulLogin(AuthResult result)
    {
        _sessionAuthResult = result;
        IsLoggedIn = true;
        IsRegistering = false;
        IsResettingPassword = false;
        CurrentView = "Main";
        StatusMessage = $"Добро пожаловать, {result.Username}!";
        Username = result.Username;
        UserEmail = result.Email;
        Password = string.Empty;
        RecoveryCode = string.Empty;
        NewPassword = string.Empty;
        ClearAuthError();
        ClearRecoveryCodeBanner();
    }

    private void CompletePasswordReset()
    {
        IsResettingPassword = false;
        Password = string.Empty;
        RecoveryCode = string.Empty;
        NewPassword = string.Empty;
        ClearAuthError();
        StatusMessage = "Пароль изменен! Войдите с новым паролем.";
    }

    private void ShowRegisterError(string? message)
    {
        SetAuthError(string.IsNullOrWhiteSpace(message) ? "Ошибка регистрации" : message.Trim(), "Ошибка регистрации");
    }

    private void ShowLoginError(string? message)
    {
        SetAuthError(string.IsNullOrWhiteSpace(message) ? "Неверное имя пользователя или пароль" : message.Trim(), "Ошибка входа");
    }

    private void ShowResetPasswordError(string? message)
    {
        SetAuthError(string.IsNullOrWhiteSpace(message) ? "Ошибка сброса пароля" : message.Trim(), "Ошибка сброса пароля");
    }

    private void ShowConnectionError(Exception ex)
    {
        SetAuthError(GetConnectionErrorMessage(ex), "Ошибка подключения");
    }

    private void FinishAuthFlow()
    {
        FinishLoginRequest();
        FinishRegisterRequest();
        FinishResetPasswordRequest();
        NotifyAuthUiStateChanged();
    }

    private void InitializeAuthUiState()
    {
        NotifyAuthUiStateChanged();
    }

    private bool IsRecoveryCodeBannerVisible() => ShowRecoveryCode && !string.IsNullOrWhiteSpace(RecoveryCodeDisplay);

    private void HideRecoveryCodeBannerForManualLoginAttempt()
    {
        if (IsRecoveryCodeBannerVisible())
        {
            ClearRecoveryCodeBanner();
        }
    }

    private void NormalizeInputsBeforeLogin()
    {
        Username = NormalizeUsername(Username);
    }

    private void NormalizeInputsBeforeRegister()
    {
        Username = NormalizeUsername(Username);
    }

    private void NormalizeInputsBeforeReset()
    {
        Username = NormalizeUsername(Username);
        RecoveryCode = NormalizeRecoveryCode(RecoveryCode);
    }

    private void PrepareForLoginValidation()
    {
        NormalizeInputsBeforeLogin();
    }

    private void PrepareForRegisterValidation()
    {
        NormalizeInputsBeforeRegister();
    }

    private void PrepareForResetValidation()
    {
        NormalizeInputsBeforeReset();
    }

    private void MarkLoginFlowStarted()
    {
        UpdateAuthBusyState(login: true);
    }

    private void MarkRegisterFlowStarted()
    {
        UpdateAuthBusyState(register: true);
    }

    private void MarkResetFlowStarted()
    {
        UpdateAuthBusyState(reset: true);
    }

    private void EnterLoginBusyState()
    {
        MarkLoginFlowStarted();
    }

    private void EnterRegisterBusyState()
    {
        MarkRegisterFlowStarted();
    }

    private void EnterResetBusyState()
    {
        MarkResetFlowStarted();
    }

    private void ExitAllBusyStates()
    {
        ResetAuthBusyState();
    }

    private bool BeginLoginRequestIfPossible()
    {
        if (IsAuthBusy || IsResettingPassword || IsRegistering)
        {
            return false;
        }

        StartLoginRequest();
        EnterLoginBusyState();
        return true;
    }

    private bool BeginRegisterRequestIfPossible()
    {
        if (IsAuthBusy || IsResettingPassword || !IsRegistering)
        {
            return false;
        }

        StartRegisterRequest();
        EnterRegisterBusyState();
        return true;
    }

    private bool BeginResetRequestIfPossible()
    {
        if (IsAuthBusy || !IsResettingPassword)
        {
            return false;
        }

        StartResetPasswordRequest();
        EnterResetBusyState();
        return true;
    }

    private void EndCurrentAuthRequest()
    {
        FinishLoginRequest();
        FinishRegisterRequest();
        FinishResetPasswordRequest();
        ExitAllBusyStates();
    }

    private void EnsureBusyHintState()
    {
        NotifyAuthUiStateChanged();
    }

    private void SetStatusForIdleAuthScreen()
    {
        if (IsResettingPassword)
        {
            StatusMessage = "Введите имя пользователя, код восстановления и новый пароль";
            return;
        }

        StatusMessage = IsRegistering ? "Регистрация нового аккаунта" : "Вход в аккаунт";
    }

    private void RefreshAuthUiAfterFlow()
    {
        EnsureBusyHintState();
    }

    private bool CanInteractWithAuthNavigation() => !IsAuthBusy;

    private bool CanSubmitLogin() => !IsAuthBusy && !IsResettingPassword && !IsRegistering;

    private bool CanSubmitRegister() => !IsAuthBusy && !IsResettingPassword && IsRegistering;

    private bool CanSubmitReset() => !IsAuthBusy && IsResettingPassword;

    private bool CanOpenResetScreen() => !IsAuthBusy && !IsResettingPassword;

    private bool CanCloseResetScreen() => !IsAuthBusy && IsResettingPassword;

    private bool CanToggleRegistrationMode() => !IsAuthBusy && !IsResettingPassword;

    private void ClearResetFormFields()
    {
        RecoveryCode = string.Empty;
        NewPassword = string.Empty;
    }

    private void ResetToLoginScreenState()
    {
        IsResettingPassword = false;
        ClearResetFormFields();
        ClearAuthError();
        StatusMessage = "Вход в аккаунт";
    }

    private void EnterResetScreenState()
    {
        IsResettingPassword = true;
        IsRegistering = false;
        ClearResetFormFields();
        ClearAuthError();
        StatusMessage = "Введите имя пользователя, код восстановления и новый пароль";
    }

    private void ToggleRegisterScreenState()
    {
        IsRegistering = !IsRegistering;
        ClearAuthError();
        StatusMessage = IsRegistering ? "Регистрация нового аккаунта" : "Вход в аккаунт";
    }

    private void ClearPasswordFieldAfterLoginFailure()
    {
        Password = string.Empty;
    }

    private void ClearPasswordFieldAfterRegisterFailure()
    {
        Password = string.Empty;
    }

    private void ClearNewPasswordFieldAfterResetFailure()
    {
        NewPassword = string.Empty;
    }

    private void PrepareForManualLoginAttempt()
    {
        HideRecoveryCodeBannerForManualLoginAttempt();
    }

    private void OnAuthScreenShown()
    {
        NotifyAuthUiStateChanged();
    }

    private void OnAuthScreenChanged()
    {
        NotifyAuthUiStateChanged();
    }

    private void SetRegistrationMode(bool value)
    {
        IsRegistering = value;
        NotifyAuthUiStateChanged();
    }

    private void SetResetMode(bool value)
    {
        IsResettingPassword = value;
        NotifyAuthUiStateChanged();
    }

    private bool HasRecoveryCode(string? recoveryCode) => !string.IsNullOrWhiteSpace(NormalizeRecoveryCode(recoveryCode));

    private string GetRegisterRecoveryCodeOrEmpty(string? recoveryCode) => NormalizeRecoveryCode(recoveryCode);

    private void ClearTransientAuthFieldsAfterSuccess()
    {
        Password = string.Empty;
        RecoveryCode = string.Empty;
        NewPassword = string.Empty;
    }

    private void PrepareForSuccessfulResetPassword()
    {
        CompletePasswordReset();
    }

    private void PrepareForAuthFailure()
    {
        NotifyAuthUiStateChanged();
    }

    private void PrepareForAuthSuccess()
    {
        NotifyAuthUiStateChanged();
    }

    private void PrepareForBusyStateChange()
    {
        NotifyAuthUiStateChanged();
    }

    private void NotifyAuthButtonsChanged()
    {
        NotifyAuthUiStateChanged();
    }

    private void SetAuthBusyFlags(bool isLogin = false, bool isRegister = false, bool isReset = false)
    {
        IsLoggingIn = isLogin;
        IsRegisteringAccount = isRegister;
        IsResettingPasswordRequest = isReset;
        NotifyAuthUiStateChanged();
    }

    private void ResetAuthBusyFlags()
    {
        SetAuthBusyFlags();
    }

    private bool StartLoginBusyFlow()
    {
        if (!CanSubmitLogin())
        {
            return false;
        }

        StartLoginRequest();
        SetAuthBusyFlags(isLogin: true);
        return true;
    }

    private bool StartRegisterBusyFlow()
    {
        if (!CanSubmitRegister())
        {
            return false;
        }

        StartRegisterRequest();
        SetAuthBusyFlags(isRegister: true);
        return true;
    }

    private bool StartResetBusyFlow()
    {
        if (!CanSubmitReset())
        {
            return false;
        }

        StartResetPasswordRequest();
        SetAuthBusyFlags(isReset: true);
        return true;
    }

    private void FinishBusyFlow()
    {
        FinishLoginRequest();
        FinishRegisterRequest();
        FinishResetPasswordRequest();
        ResetAuthBusyFlags();
    }

    private void UpdateAuthStateAfterFlow()
    {
        NotifyAuthUiStateChanged();
    }

    private void PrepareForLoginSubmission()
    {
        PrepareForLoginValidation();
        PrepareForManualLoginAttempt();
    }

    private void PrepareForRegisterSubmission()
    {
        PrepareForRegisterValidation();
    }

    private void PrepareForResetSubmission()
    {
        PrepareForResetValidation();
    }

    private void FinalizeAuthFlow()
    {
        FinishBusyFlow();
        UpdateAuthStateAfterFlow();
    }

    private void EnsureAuthModePropertiesNotified()
    {
        NotifyAuthUiStateChanged();
    }

    private void InitializeAuthScreenState()
    {
        EnsureAuthModePropertiesNotified();
    }

    private void UpdateStatusForCurrentAuthMode()
    {
        SetStatusForIdleAuthScreen();
    }

    private void ApplyPostFailureCleanup()
    {
        NotifyAuthUiStateChanged();
    }

    private void ApplyPostSuccessCleanup()
    {
        NotifyAuthUiStateChanged();
    }

    private void PrepareForAuthSubmission()
    {
        NotifyAuthUiStateChanged();
    }

    private bool IsAuthNavigationDisabled() => IsAuthBusy;

    private bool IsLoginActionDisabled() => IsAuthBusy || IsRegistering || IsResettingPassword;

    private bool IsRegisterActionDisabled() => IsAuthBusy || !IsRegistering || IsResettingPassword;

    private bool IsResetActionDisabled() => IsAuthBusy || !IsResettingPassword;

    private bool IsSecondaryAuthActionDisabled() => IsAuthBusy;

    private void ClearBannerIfNeededForLogin()
    {
        ClearRecoveryCodeBanner();
    }

    private void NormalizeAuthInputFields()
    {
        Username = NormalizeUsername(Username);
        RecoveryCode = NormalizeRecoveryCode(RecoveryCode);
    }

    private void PrepareForRegisterSuccess(AuthResult result)
    {
        var recoveryCode = NormalizeRecoveryCode(result.RecoveryCode);
        if (string.IsNullOrWhiteSpace(recoveryCode))
        {
            SetAuthError("Регистрация завершена, но код восстановления не получен. Попробуйте зарегистрироваться снова или обратитесь в поддержку.", "Ошибка регистрации");
            return;
        }

        PrepareForSuccessfulRegistration(recoveryCode);
    }

    private void PrepareForLoginSuccessResult(AuthResult result)
    {
        PrepareForSuccessfulLogin(result);
    }

    private void PrepareForResetSuccessResult()
    {
        CompletePasswordReset();
    }

    private void HandleBusyFlowStart()
    {
        NotifyAuthUiStateChanged();
    }

    private void HandleBusyFlowEnd()
    {
        NotifyAuthUiStateChanged();
    }

    private void EnsureSensitiveFieldsCleanAfterError(string mode)
    {
        switch (mode)
        {
            case "login":
                Password = string.Empty;
                break;
            case "register":
                Password = string.Empty;
                break;
            case "reset":
                NewPassword = string.Empty;
                break;
        }
    }

    private void HandleConnectionFailureForMode(string mode, Exception ex)
    {
        ShowConnectionError(ex);
        EnsureSensitiveFieldsCleanAfterError(mode);
    }

    private void HandleBackendFailureForMode(string mode, string? message)
    {
        switch (mode)
        {
            case "login":
                ShowLoginError(message);
                Password = string.Empty;
                break;
            case "register":
                ShowRegisterError(message);
                Password = string.Empty;
                break;
            case "reset":
                ShowResetPasswordError(message);
                NewPassword = string.Empty;
                break;
        }
    }

    private void InitializeAuthHints()
    {
        NotifyAuthUiStateChanged();
    }

    private void RefreshAuthHints()
    {
        NotifyAuthUiStateChanged();
    }

    private void KeepRecoveryCodeBannerVisibleUntilLogin()
    {
        if (!IsRecoveryCodeBannerVisible())
        {
            return;
        }
    }

    private void DismissRecoveryCodeBannerOnLogin()
    {
        ClearRecoveryCodeBanner();
    }

    private void UpdateBusyTextBindings()
    {
        NotifyAuthUiStateChanged();
    }

    private void SetBusyForMode(string mode)
    {
        switch (mode)
        {
            case "login":
                SetAuthBusyFlags(isLogin: true);
                break;
            case "register":
                SetAuthBusyFlags(isRegister: true);
                break;
            case "reset":
                SetAuthBusyFlags(isReset: true);
                break;
        }
    }

    private void ResetBusyForMode()
    {
        ResetAuthBusyFlags();
    }

    private void ClearTransientAuthState()
    {
        NotifyAuthUiStateChanged();
    }

    private void PrepareForTransitionToLogin()
    {
        SetResetMode(false);
        ClearResetFormFields();
    }

    private void PrepareForTransitionToReset()
    {
        SetResetMode(true);
        SetRegistrationMode(false);
        ClearResetFormFields();
    }

    private void PrepareForTransitionToRegisterToggle()
    {
        SetRegistrationMode(!IsRegistering);
    }

    private void HandleAuthUiStateMutation()
    {
        NotifyAuthUiStateChanged();
    }

    private void EnsureAuthUiInitialized()
    {
        NotifyAuthUiStateChanged();
    }

    private void SetResetInstructions()
    {
        StatusMessage = "Введите имя пользователя, код восстановления и новый пароль";
    }

    private void SetLoginInstructions()
    {
        StatusMessage = "Вход в аккаунт";
    }

    private void SetRegisterInstructions()
    {
        StatusMessage = "Регистрация нового аккаунта";
    }

    private void PrepareForShowingResetScreen()
    {
        SetResetMode(true);
        SetRegistrationMode(false);
        ClearResetFormFields();
        ClearAuthError();
        SetResetInstructions();
    }

    private void PrepareForCancelResetScreen()
    {
        SetResetMode(false);
        ClearResetFormFields();
        ClearAuthError();
        SetLoginInstructions();
    }

    private void PrepareForToggleRegisterScreen()
    {
        SetRegistrationMode(!IsRegistering);
        ClearAuthError();
        if (IsRegistering)
        {
            SetRegisterInstructions();
        }
        else
        {
            SetLoginInstructions();
        }
    }

    private bool IsBusyWithAnyAuthFlow() => IsAuthBusy;

    private void OnBusyAuthFlowChanged()
    {
        NotifyAuthUiStateChanged();
    }

    private void InitializeAuthBusyBindings()
    {
        NotifyAuthUiStateChanged();
    }

    private void SetPasswordResetSuccessState()
    {
        CompletePasswordReset();
    }

    private void SetRegistrationSuccessState(string recoveryCode)
    {
        PrepareForSuccessfulRegistration(recoveryCode);
    }

    private void SetLoginSuccessState(AuthResult result)
    {
        PrepareForSuccessfulLogin(result);
    }

    private void SetGenericConnectionError(Exception ex)
    {
        ShowConnectionError(ex);
    }

    private void ClearRecoveredStateForLoginAttempt()
    {
        ClearRecoveryCodeBanner();
    }

    private void SetAuthUiStateAfterCompletion()
    {
        NotifyAuthUiStateChanged();
    }

    private void ResetRequestFlagsAndNotify()
    {
        FinishBusyFlow();
    }

    private void ApplyNormalizedFieldsForAuth()
    {
        NormalizeAuthInputFields();
    }

    private bool IsResetFormVisible() => IsResettingPassword;

    private bool IsRegisterFormVisible() => IsRegistering;

    private bool IsLoginFormVisible() => !IsResettingPassword && !IsRegistering;

    private void EnsureFormVisibilityConsistency()
    {
        if (IsResettingPassword)
        {
            IsRegistering = false;
        }
    }

    private void ApplyAuthScreenConsistency()
    {
        EnsureFormVisibilityConsistency();
    }

    private bool ShouldPreventAuthAction() => IsAuthBusy;

    private void PrepareAuthActionExecution()
    {
        ApplyAuthScreenConsistency();
        NotifyAuthUiStateChanged();
    }

    private void FinishAuthActionExecution()
    {
        NotifyAuthUiStateChanged();
    }

    private void ResetTransientErrorState()
    {
        ClearAuthError();
    }

    private void PrepareForResettingPassword()
    {
        RecoveryCode = NormalizeRecoveryCode(RecoveryCode);
    }

    private void PrepareForLoginingIn()
    {
        Username = NormalizeUsername(Username);
    }

    private void PrepareForRegisteringAccount()
    {
        Username = NormalizeUsername(Username);
    }

    private void RefreshAuthBindings()
    {
        NotifyAuthUiStateChanged();
    }

    private void EnsureAuthScreenNotBusy()
    {
        NotifyAuthUiStateChanged();
    }

    private async Task ConfirmResetPasswordAsync()
    {
        if (IsAuthBusy || !IsResettingPassword)
        {
            return;
        }

        ClearAuthError();
        if (!ValidateResetPasswordInputs())
        {
            return;
        }

        StartResetPasswordRequest();
        NotifyAuthUiStateChanged();

        try
        {
            Console.WriteLine("[ConfirmResetPasswordAsync] Подтверждение сброса");
            ApiResponse<string> result;
            if (IsForcedPasswordReset)
            {
                result = await _apiService.ResetPasswordByAdminAsync(Username, RecoveryCode, NewPassword);
            }
            else
            {
                result = await _apiService.ResetPasswordAsync(Username, RecoveryCode, NewPassword);
            }

            if (result.IsSuccess)
            {
                CompletePasswordReset();
                IsForcedPasswordReset = false;
                ForcedPasswordResetMessage = string.Empty;
                Console.WriteLine("[ConfirmResetPasswordAsync] Пароль изменен");
                return;
            }

            ShowResetPasswordError(result.ErrorMessage);
            NewPassword = string.Empty;
            Console.WriteLine($"[ConfirmResetPasswordAsync] Ошибка: {result.ErrorMessage}");
        }
        catch (Exception ex)
        {
            ShowConnectionError(ex);
            NewPassword = string.Empty;
            Console.WriteLine($"[ConfirmResetPasswordAsync] EXCEPTION: {ex.Message}");
        }
        finally
        {
            FinishAuthFlow();
        }
    }

    private async Task RegisterAsync()
    {
        if (IsAuthBusy || IsResettingPassword || !IsRegistering)
        {
            return;
        }

        ClearAuthError();
        if (!ValidateRegisterInputs())
        {
            return;
        }

        StartRegisterRequest();
        NotifyAuthUiStateChanged();

        try
        {
            Console.WriteLine($"[RegisterAsync] Отправка запроса: {Username}");
            var result = await _apiService.RegisterAsync(Username, Password);

            if (result.IsSuccess && result.Data != null)
            {
                var recoveryCode = NormalizeRecoveryCode(result.Data.RecoveryCode);
                if (string.IsNullOrWhiteSpace(recoveryCode))
                {
                    ShowRegisterError("Регистрация завершена, но код восстановления не получен. Обратитесь в поддержку.");
                    return;
                }

                PrepareForSuccessfulRegistration(recoveryCode);
                Console.WriteLine("[RegisterAsync] Регистрация успешна");
                return;
            }

            ShowRegisterError(result.ErrorMessage);
            Password = string.Empty;
            Console.WriteLine($"[RegisterAsync] Ошибка: {result.ErrorMessage}");
        }
        catch (Exception ex)
        {
            ShowConnectionError(ex);
            Password = string.Empty;
            Console.WriteLine($"[RegisterAsync] EXCEPTION: {ex.Message}");
        }
        finally
        {
            FinishAuthFlow();
        }
    }

    private async Task LoginAsync()
    {
        if (IsAuthBusy || IsResettingPassword || IsRegistering)
        {
            return;
        }

        ClearAuthError();
        if (!ValidateLoginInputs())
        {
            return;
        }

        StartLoginRequest();
        NotifyAuthUiStateChanged();

        try
        {
            Console.WriteLine($"[LoginAsync] Отправка запроса: {Username}");
            var result = await _apiService.LoginAsync(Username, Password);

            if (result.IsSuccess && result.Data != null)
            {
                PrepareForSuccessfulLogin(result.Data);
                SaveToken(result.Data.Token ?? string.Empty, result.Data.Username, result.Data.Email, result.Data.MinecraftUUID);
                Console.WriteLine("[LoginAsync] Вход успешен");

                CheckInstallation();
                await CheckModpackVersionAsync();
                await LoadProfileAsync();
                await LoadServerStatusAsync();
                await LoadCurrentSkinAsync();
                await RefreshAdminAccessAsync();
                return;
            }

            if (result.RequiresPasswordReset)
            {
                IsResettingPassword = true;
                IsForcedPasswordReset = true;
                IsRegistering = false;
                RecoveryCode = string.Empty;
                NewPassword = string.Empty;
                ForcedPasswordResetMessage = string.IsNullOrWhiteSpace(result.NotificationMessage)
                    ? "Ваш пароль был сброшен администратором. Задайте новый пароль, чтобы продолжить."
                    : result.NotificationMessage;
                SetAuthError(ForcedPasswordResetMessage, "Требуется смена пароля");
                Console.WriteLine("[LoginAsync] Требуется принудительная смена пароля");
                return;
            }

            ShowLoginError(result.ErrorMessage);
            Password = string.Empty;
            Console.WriteLine($"[LoginAsync] Ошибка: {result.ErrorMessage}");
        }
        catch (Exception ex)
        {
            ShowConnectionError(ex);
            Password = string.Empty;
            Console.WriteLine($"[LoginAsync] EXCEPTION: {ex.Message}");
        }
        finally
        {
            FinishAuthFlow();
        }
    }

    private async Task LoginAsync_Old()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                StatusMessage = "Введите имя выжившего!";
                return;
            }

            StatusMessage = "Аутентификация...";
            var authResult = _authService.AuthenticateOffline(Username);

            IsLoggedIn = true;
            CurrentView = "Main";
            StatusMessage = $"Добро пожаловать, {authResult.Username}!";

            // Проверяем установку
            CheckInstallation();

            // Проверяем версию сборки
            await CheckModpackVersionAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка входа: {ex.Message}";
        }
    }

    private async Task CheckModpackVersionAsync()
    {
        try
        {
            var currentVersion = await _modpackUpdater.GetCurrentVersionAsync();
            ModpackVersion = $"Сборка: v{currentVersion}";

            // Проверяем наличие обновлений
            var hasUpdate = await _modpackUpdater.CheckForUpdatesAsync();
            if (hasUpdate)
            {
                StatusMessage = "Доступно обновление сборки!";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CheckModpackVersion] Error: {ex.Message}");
            ModpackVersion = "Сборка: не установлена";
        }
    }

    private async Task UpdateModpackAsync()
    {
        try
        {
            StatusMessage = "Проверка обновлений сборки...";
            ProgressValue = 0;

            var hasUpdate = await _modpackUpdater.CheckForUpdatesAsync();

            if (!hasUpdate)
            {
                StatusMessage = "У вас установлена последняя версия сборки";
                return;
            }

            var success = await _modpackUpdater.DownloadAndInstallModpackAsync();

            if (success)
            {
                StatusMessage = "Сборка успешно обновлена!";
                await CheckModpackVersionAsync();
            }
            else
            {
                StatusMessage = "Ошибка обновления сборки";
            }

            ProgressValue = 0;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка обновления: {ex.Message}";
            Console.WriteLine($"[UpdateModpackAsync] ERROR: {ex.Message}");
            ProgressValue = 0;
        }
    }

    private async Task InstallMinecraftAsync()
    {
        try
        {
            Console.WriteLine($"[InstallMinecraftAsync] Starting installation to: {_minecraftDirectory}");
            StatusMessage = "Начинаем установку...";
            ProgressValue = 0;

            Console.WriteLine("[InstallMinecraftAsync] Calling InstallMinecraftAsync...");
            var success = await _installer.InstallMinecraftAsync();

            if (success)
            {
                Console.WriteLine("[InstallMinecraftAsync] Minecraft installed, installing Forge...");
                StatusMessage = "Установка Forge...";

                bool forgeSuccess = false;
                try
                {
                    forgeSuccess = await _installer.InstallForgeAsync();
                    if (forgeSuccess)
                    {
                        Console.WriteLine("[InstallMinecraftAsync] Forge installed successfully!");
                        StatusMessage = "Forge установлен!";
                    }
                    else
                    {
                        Console.WriteLine("[InstallMinecraftAsync] Forge installation failed!");
                        StatusMessage = "Ошибка установки Forge. Игра будет запущена в vanilla режиме.";
                    }
                }
                catch (Exception forgeEx)
                {
                    Console.WriteLine($"[InstallMinecraftAsync] Forge installation exception: {forgeEx.Message}");
                    Console.WriteLine($"Stack trace: {forgeEx.StackTrace}");
                    StatusMessage = $"Ошибка Forge: {forgeEx.Message}. Игра будет в vanilla режиме.";
                }

                // Скачиваем модпак после установки Forge
                if (forgeSuccess)
                {
                    Console.WriteLine("[InstallMinecraftAsync] Downloading modpack...");
                    StatusMessage = "Скачивание сборки модов...";
                    ProgressValue = 0;

                    try
                    {
                        var modpackSuccess = await _modpackUpdater.DownloadAndInstallModpackAsync();
                        if (modpackSuccess)
                        {
                            Console.WriteLine("[InstallMinecraftAsync] Modpack installed successfully!");
                            StatusMessage = "Установка завершена! Готов к запуску.";
                            await CheckModpackVersionAsync();
                        }
                        else
                        {
                            Console.WriteLine("[InstallMinecraftAsync] Modpack installation failed!");
                            StatusMessage = "Ошибка установки сборки. Используйте кнопку 'Обновить сборку'.";
                        }
                    }
                    catch (Exception modpackEx)
                    {
                        Console.WriteLine($"[InstallMinecraftAsync] Modpack installation exception: {modpackEx.Message}");
                        Console.WriteLine($"Stack trace: {modpackEx.StackTrace}");
                        StatusMessage = $"Ошибка сборки: {modpackEx.Message}. Используйте кнопку 'Обновить сборку'.";
                    }
                }
                else
                {
                    StatusMessage = "Forge не установлен. Сборка не будет загружена.";
                }

                IsInstalled = true;
                Console.WriteLine("[InstallMinecraftAsync] Installation complete!");
            }
            else
            {
                StatusMessage = "Ошибка установки. Проверьте подключение к сети.";
                Console.WriteLine("[InstallMinecraftAsync] Installation failed!");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Критическая ошибка: {ex.Message}";
            Console.WriteLine($"[InstallMinecraftAsync] EXCEPTION: {ex.Message}");
            Console.WriteLine($"[InstallMinecraftAsync] Stack trace: {ex.StackTrace}");
        }
    }

    private async Task LaunchGameAsync()
    {
        try
        {
            StatusMessage = "Подготовка к запуску...";
            GameOutput = string.Empty;

            // Скачиваем authlib-injector если его нет
            await DownloadAuthlibInjectorIfNeededAsync();

            // Для offline-mode серверов UUID должен строго совпадать с алгоритмом OfflinePlayer:<name>.
            // Поэтому даже при API-сессии берём access token из сессии, но UUID пересчитываем локально.
            var authResult = _sessionAuthResult ?? _authService.AuthenticateOffline(Username);
            var offlineIdentity = _authService.AuthenticateOffline(Username);
            authResult.UUID = offlineIdentity.UUID;
            authResult.MinecraftUUID = offlineIdentity.UUID;

            // CreateLaunchOptions автоматически определит Forge или vanilla
            var launchOptions = _installer.CreateLaunchOptions(authResult);

            // Устанавливаем полноэкранный режим
            launchOptions.IsFullscreen = IsFullscreen;

            // Устанавливаем выделенную RAM
            launchOptions.MaxMemory = _allocatedRamGB * 1024;
            launchOptions.MinMemory = Math.Min(512, _allocatedRamGB * 512);

            // Извлекаем нативные библиотеки перед запуском
            StatusMessage = "Извлечение нативных библиотек...";
            _installer.ExtractNatives(launchOptions.Version);

            // Чиним битые/пропущенные ассеты (частая причина Missing sound/texture warnings).
            StatusMessage = "Проверка ассетов...";
            await _installer.VerifyAndRepairAssetsAsync();

            StatusMessage = "Запуск игры...";
            _gameLauncher.LaunchGame(launchOptions);

            StatusMessage = launchOptions.Version.Contains("forge") ? "Forge запущен! Выживайте..." : "Игра запущена! Выживайте...";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка запуска: {ex.Message}";
            Console.WriteLine($"[LaunchGameAsync] ERROR: {ex.Message}");
            Console.WriteLine($"[LaunchGameAsync] Stack trace: {ex.StackTrace}");
            IsGameRunning = false;
        }
    }

    private async Task DownloadAuthlibInjectorIfNeededAsync()
    {
        try
        {
            var authlibPath = Path.Combine(_minecraftDirectory, "authlib-injector.jar");

            if (File.Exists(authlibPath))
            {
                Console.WriteLine("[DownloadAuthlibInjector] Уже установлен");
                return;
            }

            Console.WriteLine("[DownloadAuthlibInjector] Скачивание authlib-injector...");

            // Скачиваем последнюю версию authlib-injector
            var authlibUrl = "https://github.com/yushijinhun/authlib-injector/releases/download/v1.2.5/authlib-injector-1.2.5.jar";
            var authlibBytes = await _httpClient.GetByteArrayAsync(authlibUrl);
            await File.WriteAllBytesAsync(authlibPath, authlibBytes);

            Console.WriteLine($"[DownloadAuthlibInjector] Установлен: {authlibPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DownloadAuthlibInjector] Ошибка: {ex.Message}");
        }
    }

    private void CheckInstallation()
    {
        // Проверяем Forge версию (приоритет)
        var forgeJsonPath = Path.Combine(_minecraftDirectory, "versions", "1.20.1-forge-47.3.0", "1.20.1-forge-47.3.0.json");
        var vanillaJarPath = Path.Combine(_minecraftDirectory, "versions", "1.20.1", "1.20.1.jar");

        bool forgeInstalled = File.Exists(forgeJsonPath);
        bool vanillaInstalled = File.Exists(vanillaJarPath);

        IsInstalled = vanillaInstalled; // Для запуска нужна хотя бы vanilla

        if (forgeInstalled && vanillaInstalled)
            StatusMessage = "Minecraft 1.20.1 Forge установлен. Готов к запуску.";
        else if (vanillaInstalled)
            StatusMessage = "Minecraft установлен. Нажмите 'Проверить файлы' для установки Forge.";
        else
            StatusMessage = "Требуется установка Minecraft 1.20.1 Forge";
    }

    private async Task LoadProfileAsync()
    {
        try
        {
            var result = await _apiService.GetProfileAsync();
            if (result.IsSuccess && result.Data != null)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    PlayTimeMinutes = result.Data.PlayTimeMinutes;
                    if (result.Data.RequiresPasswordReset)
                    {
                        IsForcedPasswordReset = true;
                        IsResettingPassword = true;
                        IsRegistering = false;
                        ForcedPasswordResetMessage = "Ваш пароль был сброшен администратором. Задайте новый пароль.";
                        CurrentView = "Login";
                        IsLoggedIn = false;
                        StatusMessage = "Требуется смена пароля перед входом в игру";
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LoadProfile] Ошибка: {ex.Message}");
        }
    }

    private async Task LoadServerStatusAsync()
    {
        try
        {
            var result = await _apiService.GetServerStatusAsync();
            if (result.IsSuccess && result.Data != null)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsServerOnline = result.Data.IsOnline;
                    PlayersOnline = result.Data.PlayersOnline;
                    MaxPlayers = result.Data.MaxPlayers;
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LoadServerStatus] Ошибка: {ex.Message}");
        }
    }

    private async Task LoadCurrentSkinAsync()
    {
        try
        {
            // Сначала проверяем локальный кэш скина
            var localSkinPath = GetLocalSkinPath();
            if (File.Exists(localSkinPath))
            {
                Console.WriteLine($"[LoadCurrentSkinAsync] Загрузка скина из кэша: {localSkinPath}");
                await LoadSkinPreviewAsync(localSkinPath);

                // Копируем скин в директорию Minecraft для отображения в игре
                await CopySkinToMinecraftAsync(localSkinPath);

                var cachedSkinType = LoadLocalSkinType();
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    CurrentSkinPath = localSkinPath;
                    ApplySkinType(cachedSkinType);
                    SkinStatus = $"Скин загружен из кэша ({cachedSkinType})";
                });
            }

            // Затем пытаемся обновить с сервера
            var result = await _apiService.GetCurrentSkinAsync();
            if (result.IsSuccess && result.Data != null)
            {
                // Скачиваем скин по URL
                var skinBytes = await _httpClient.GetByteArrayAsync(result.Data.DownloadUrl);

                // Сохраняем локально для кэша
                var skinDir = Path.GetDirectoryName(localSkinPath);
                if (!string.IsNullOrEmpty(skinDir))
                {
                    Directory.CreateDirectory(skinDir);
                }
                await File.WriteAllBytesAsync(localSkinPath, skinBytes);

                // Копируем скин в директорию Minecraft для отображения в игре
                await CopySkinToMinecraftAsync(localSkinPath);

                // Загружаем превью
                await LoadSkinPreviewAsync(localSkinPath);

                SaveLocalSkinType(result.Data.SkinType);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    CurrentSkinPath = localSkinPath;
                    ApplySkinType(result.Data.SkinType);
                    SkinStatus = $"Скин загружен ({result.Data.SkinType})";
                });

                Console.WriteLine($"[LoadCurrentSkinAsync] Скин загружен с сервера: {result.Data.SkinType}");
            }
            else if (!File.Exists(localSkinPath))
            {
                // Нет ни локального, ни серверного скина
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    CurrentSkinPath = null;
                    CurrentSkinPreview = null;
                    SkinStatus = "Скин не загружен";
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LoadCurrentSkinAsync] Ошибка: {ex.Message}");

            // Если ошибка сети, но есть локальный кэш - используем его
            var localSkinPath = GetLocalSkinPath();
            if (File.Exists(localSkinPath))
            {
                try
                {
                    await CopySkinToMinecraftAsync(localSkinPath);
                    await LoadSkinPreviewAsync(localSkinPath);
                    var cachedSkinType = LoadLocalSkinType();
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        CurrentSkinPath = localSkinPath;
                        ApplySkinType(cachedSkinType);
                        SkinStatus = $"Скин загружен из кэша (офлайн, {cachedSkinType})";
                    });
                }
                catch (Exception cacheEx)
                {
                    Console.WriteLine($"[LoadCurrentSkinAsync] Ошибка загрузки из кэша: {cacheEx.Message}");
                }
            }
        }
    }

    private async Task CopySkinToMinecraftAsync(string skinPath)
    {
        try
        {
            // Копируем скин для authlib-injector не нужно - он загружает с сервера
            // Но сохраняем локально для превью в лаунчере
            Console.WriteLine($"[CopySkinToMinecraft] Скин сохранен локально для превью");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CopySkinToMinecraft] Ошибка: {ex.Message}");
        }
    }

    private string GetLocalSkinPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var launcherDir = Path.Combine(appData, "SRP-RP-Launcher", "cache");
        return Path.Combine(launcherDir, $"{Username}_skin.png");
    }

    private string GetLocalSkinTypePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var launcherDir = Path.Combine(appData, "SRP-RP-Launcher", "cache");
        return Path.Combine(launcherDir, $"{Username}_skin_type.txt");
    }

    private void SaveLocalSkinType(string skinType)
    {
        try
        {
            var skinTypePath = GetLocalSkinTypePath();
            var skinDir = Path.GetDirectoryName(skinTypePath);
            if (!string.IsNullOrEmpty(skinDir))
            {
                Directory.CreateDirectory(skinDir);
            }

            File.WriteAllText(skinTypePath, skinType);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SaveLocalSkinType] Ошибка: {ex.Message}");
        }
    }

    private void ApplySkinType(string skinType)
    {
        IsClassicSkin = skinType != "slim";
        IsSlimSkin = skinType == "slim";
    }

    private string LoadLocalSkinType()
    {
        try
        {
            var skinTypePath = GetLocalSkinTypePath();
            if (File.Exists(skinTypePath))
            {
                var skinType = File.ReadAllText(skinTypePath).Trim().ToLowerInvariant();
                if (skinType == "slim")
                {
                    return "slim";
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LoadLocalSkinType] Ошибка: {ex.Message}");
        }

        return "classic";
    }

    private void DeleteLocalSkinType()
    {
        try
        {
            var skinTypePath = GetLocalSkinTypePath();
            if (File.Exists(skinTypePath))
            {
                File.Delete(skinTypePath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DeleteLocalSkinType] Ошибка: {ex.Message}");
        }
    }

    private void StartEditNickname()
    {
        NewNickname = Username;
        IsEditingNickname = true;
    }

    private void CancelEditNickname()
    {
        IsEditingNickname = false;
        NewNickname = "";
        LoginErrorMessage = null;
    }

    private async Task SaveNicknameAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NewNickname))
            {
                LoginErrorMessage = "Введите новый никнейм";
                return;
            }

            if (NewNickname.Length < 3 || NewNickname.Length > 16)
            {
                LoginErrorMessage = "Никнейм должен быть от 3 до 16 символов";
                return;
            }

            StatusMessage = "Изменение никнейма...";
            var result = await _apiService.ChangeUsernameAsync(NewNickname);

            if (result.IsSuccess)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Username = NewNickname;
                    IsEditingNickname = false;
                    NewNickname = "";
                    LoginErrorMessage = null;
                    StatusMessage = "Никнейм успешно изменен!";
                });

                // Обновляем сохраненный токен с новым никнеймом
                var tokenFile = GetTokenFilePath();
                if (File.Exists(tokenFile))
                {
                    var data = File.ReadAllText(tokenFile).Split('|');
                    if (data.Length == 3)
                    {
                        SaveToken(data[0], Username, data[2]);
                    }
                }
            }
            else
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = result.ErrorMessage ?? "Ошибка смены никнейма";
                    StatusMessage = "Ошибка смены никнейма";
                });
            }
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoginErrorMessage = $"Ошибка: {ex.Message}";
                StatusMessage = "Ошибка смены никнейма";
            });
        }
    }

    private async Task CheckForLauncherUpdatesAsync()
    {
        try
        {
            await Task.Delay(2000); // Небольшая задержка после запуска

            var (hasUpdate, latestVersion, downloadUrl) = await _updateService.CheckForUpdatesAsync();

            if (hasUpdate)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    HasLauncherUpdate = true;
                    LatestLauncherVersion = latestVersion;
                    _launcherUpdateUrl = downloadUrl;
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CheckForLauncherUpdatesAsync] Ошибка: {ex.Message}");
        }
    }

    private async Task UpdateLauncherAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_launcherUpdateUrl))
            {
                StatusMessage = "Ошибка: URL обновления не найден";
                return;
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = "Обновление лаунчера...";
            });

            _updateService.StatusChanged += (s, status) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    StatusMessage = status;
                });
            };

            _updateService.ProgressChanged += (s, progress) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ProgressValue = progress;
                });
            };

            await _updateService.DownloadAndInstallUpdateAsync(_launcherUpdateUrl);
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Ошибка обновления: {ex.Message}";
            });
            Console.WriteLine($"[UpdateLauncherAsync] Ошибка: {ex.Message}");
        }
    }

    // Методы для работы со скинами
    private async Task UploadSkinAsync()
    {
        try
        {
            SkinStatus = "Выберите PNG файл скина 64x64 пикселей";

            // Открываем диалог выбора файла
            var dialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Выберите файл скина (PNG 64x64)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("PNG изображения")
                    {
                        Patterns = new[] { "*.png" }
                    }
                }
            };

            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel == null)
            {
                SkinStatus = "Ошибка: не удалось получить окно";
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(dialog);

            if (files.Count == 0)
            {
                SkinStatus = "Выбор файла отменен";
                return;
            }

            var filePath = files[0].Path.LocalPath;

            // Валидация файла
            if (!_skinService.ValidateSkinFile(filePath, out var error))
            {
                SkinStatus = $"Ошибка: {error}";
                return;
            }

            SkinStatus = "Загрузка скина на сервер...";

            var skinType = IsClassicSkin ? "classic" : "slim";
            var success = await _skinService.UploadSkinAsync(filePath, skinType);

            if (success)
            {
                SkinStatus = "Скин успешно загружен!";

                // Сохраняем скин в локальный кэш
                var localSkinPath = GetLocalSkinPath();
                var skinDir = Path.GetDirectoryName(localSkinPath);
                if (!string.IsNullOrEmpty(skinDir))
                {
                    Directory.CreateDirectory(skinDir);
                }
                File.Copy(filePath, localSkinPath, overwrite: true);
                SaveLocalSkinType(skinType);
                CurrentSkinPath = localSkinPath;

                // Загружаем превью из загруженного файла
                await LoadSkinPreviewAsync(localSkinPath);

                StatusMessage = "Скин успешно загружен!";
            }
            else
            {
                SkinStatus = "Ошибка загрузки скина";
            }
        }
        catch (Exception ex)
        {
            SkinStatus = $"Ошибка: {ex.Message}";
            Console.WriteLine($"[UploadSkinAsync] Ошибка: {ex.Message}");
        }
    }

    public async Task UploadSkinFromFileAsync(string filePath)
    {
        try
        {
            var skinType = IsClassicSkin ? "classic" : "slim";
            var success = await _skinService.UploadSkinAsync(filePath, skinType);

            if (success)
            {
                var localSkinPath = GetLocalSkinPath();
                var skinDir = Path.GetDirectoryName(localSkinPath);
                if (!string.IsNullOrEmpty(skinDir))
                {
                    Directory.CreateDirectory(skinDir);
                }

                File.Copy(filePath, localSkinPath, overwrite: true);
                SaveLocalSkinType(skinType);
                CurrentSkinPath = localSkinPath;
                await LoadSkinPreviewAsync(localSkinPath);
                SkinStatus = $"Скин загружен ({skinType})";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка загрузки скина: {ex.Message}";
            Console.WriteLine($"[UploadSkinFromFileAsync] Ошибка: {ex.Message}");
        }
    }

    private async Task UploadCapeAsync()
    {
        try
        {
            SkinStatus = "Выберите PNG файл плаща 64x32 пикселей";

            var dialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Выберите файл плаща (PNG 64x32)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("PNG изображения")
                    {
                        Patterns = new[] { "*.png" }
                    }
                }
            };

            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel == null)
            {
                SkinStatus = "Ошибка: не удалось получить окно";
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(dialog);

            if (files.Count == 0)
            {
                SkinStatus = "Выбор файла отменен";
                return;
            }

            var filePath = files[0].Path.LocalPath;

            if (!_skinService.ValidateCapeFile(filePath, out var error))
            {
                SkinStatus = $"Ошибка: {error}";
                return;
            }

            SkinStatus = "Загрузка плаща на сервер...";

            var success = await _skinService.UploadCapeAsync(filePath);

            if (success)
            {
                SkinStatus = "Плащ успешно загружен!";
            }
            else
            {
                SkinStatus = "Ошибка загрузки плаща";
            }
        }
        catch (Exception ex)
        {
            SkinStatus = $"Ошибка: {ex.Message}";
            Console.WriteLine($"[UploadCapeAsync] Ошибка: {ex.Message}");
        }
    }

    public async Task UploadCapeFromFileAsync(string filePath)
    {
        try
        {
            var success = await _skinService.UploadCapeAsync(filePath);

            if (success)
            {
                SkinStatus = "Плащ загружен";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка загрузки плаща: {ex.Message}";
            Console.WriteLine($"[UploadCapeFromFileAsync] Ошибка: {ex.Message}");
        }
    }

    private async Task DeleteSkinAsync()
    {
        try
        {
            SkinStatus = "Удаление скина...";

            var success = await _skinService.DeleteCurrentSkinAsync();

            if (success)
            {
                SkinStatus = "Скин удален";
                CurrentSkinPath = null;
                CurrentSkinPreview = null;

                DeleteLocalSkinType();

                // Удаляем локальный кэш
                var localSkinPath = GetLocalSkinPath();
                if (File.Exists(localSkinPath))
                {
                    File.Delete(localSkinPath);
                    Console.WriteLine($"[DeleteSkinAsync] Локальный кэш удален: {localSkinPath}");
                }
            }
            else
            {
                SkinStatus = "Ошибка удаления скина";
            }
        }
        catch (Exception ex)
        {
            SkinStatus = $"Ошибка удаления скина: {ex.Message}";
            Console.WriteLine($"[DeleteSkinAsync] Ошибка: {ex.Message}");
        }
    }

    private async Task LoadSkinPreviewAsync(string filePath)
    {
        if (_isLoadingSkinPreview)
            return;

        _isLoadingSkinPreview = true;

        try
        {
            Console.WriteLine($"[LoadSkinPreviewAsync] Загрузка превью из: {filePath}");

            // Проверяем существование файла
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[LoadSkinPreviewAsync] Файл не найден: {filePath}");
                _isLoadingSkinPreview = false;
                return;
            }

            // Сразу рендерим локально без запросов к API
            await Task.Run(() =>
            {
                try
                {
                    var renderedBytes = SkinRenderer.RenderSkin3D(filePath);

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        using var ms = new MemoryStream(renderedBytes);
                        var bitmap = new Avalonia.Media.Imaging.Bitmap(ms);
                        CurrentSkinPreview = bitmap;
                        Console.WriteLine($"[LoadSkinPreviewAsync] Превью успешно загружено");
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LoadSkinPreviewAsync] Ошибка рендеринга: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LoadSkinPreviewAsync] Ошибка: {ex.Message}");
        }
        finally
        {
            _isLoadingSkinPreview = false;
        }
    }

    private async Task RefreshLogsAsync()
    {
        try
        {
            LogStatus = "Обновление логов...";

            // Находим последний лог файл
            var logsDir = Path.Combine(Environment.CurrentDirectory, "logs");
            if (!Directory.Exists(logsDir))
            {
                GameLogs = "Папка с логами не найдена. Запустите игру для создания логов.";
                LogStatus = "Логи не найдены";
                return;
            }

            var logFiles = Directory.GetFiles(logsDir, "game_launch_*.log")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToArray();

            if (logFiles.Length == 0)
            {
                GameLogs = "Логи игры не найдены. Запустите игру для создания логов.";
                LogStatus = "Логи не найдены";
                return;
            }

            var latestLog = logFiles[0];
            var logContent = await File.ReadAllTextAsync(latestLog);

            GameLogs = logContent;
            LogStatus = $"Обновлено: {Path.GetFileName(latestLog)} ({new FileInfo(latestLog).Length / 1024} KB)";
        }
        catch (Exception ex)
        {
            GameLogs = $"Ошибка загрузки логов: {ex.Message}";
            LogStatus = "Ошибка загрузки";
            Console.WriteLine($"[RefreshLogsAsync] Ошибка: {ex.Message}");
        }
    }

    private async Task AnalyzeLogsWithAIAsync()
    {
        try
        {
            IsAnalyzingLogs = true;
            LogStatus = "Анализ логов с помощью AI...";

            if (string.IsNullOrWhiteSpace(GameLogs) || GameLogs.Contains("не найдены"))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = "Сначала загрузите логи игры";
                    LogStatus = "Нет логов для анализа";
                });
                return;
            }

            var analysisResult = await AnalyzeLogsWithClaudeAsync(GameLogs);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LogAnalysisResult = analysisResult;
                IsLogAnalysisVisible = true;
                LogStatus = "Анализ завершен";
                StatusMessage = "AI анализ логов завершен";
            });
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Ошибка AI анализа: {ex.Message}";
                LogStatus = "Ошибка анализа";
            });
            Console.WriteLine($"[AnalyzeLogsWithAIAsync] Ошибка: {ex.Message}");
        }
        finally
        {
            IsAnalyzingLogs = false;
        }
    }

    private void CloseLogAnalysis()
    {
        IsLogAnalysisVisible = false;
    }

    private async Task<string> AnalyzeLogsWithClaudeAsync(string logs)
    {
        try
        {
            // Отправляем логи на backend для анализа
            var requestBody = new
            {
                logs = logs
            };

            var response = await _httpClient.PostAsync(
                "https://srp-rp-launcher-production.up.railway.app/api/LogAnalyzer/analyze",
                new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(requestBody),
                    System.Text.Encoding.UTF8,
                    "application/json"
                )
            );

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return $"Ошибка сервера: {response.StatusCode}\n{error}";
            }

            var result = await response.Content.ReadAsStringAsync();
            var json = System.Text.Json.JsonDocument.Parse(result);

            if (json.RootElement.TryGetProperty("analysis", out var analysisElement))
            {
                return analysisElement.GetString() ?? "AI не смог проанализировать логи";
            }

            return "Неожиданный формат ответа от сервера";
        }
        catch (Exception ex)
        {
            return $"Ошибка анализа: {ex.Message}\n\nПопробуйте позже или обратитесь в поддержку Discord.";
        }
    }
}

public class AdminUserItem : ReactiveObject
{
    private bool _isBanned;

    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsWhitelisted { get; set; }
    public bool RequiresPasswordReset { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public bool IsBanned
    {
        get => _isBanned;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _isBanned, value))
            {
                this.RaisePropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText => IsBanned
        ? "Заблокирован"
        : (RequiresPasswordReset ? "Требуется смена пароля" : (IsActive ? "Активен" : "Отключен"));
    public string LastLoginText => LastLoginAt?.ToLocalTime().ToString("g") ?? "никогда";
}
