using System;
using System.IO;
using System.Reactive;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ApocalypseLauncher.Core.Models;
using ApocalypseLauncher.Core.Services;
using ApocalypseLauncher.Core.Security;
using ReactiveUI;
using Avalonia.Controls;

namespace ApocalypseLauncher.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private MinecraftInstaller _installer;
    private readonly GameLauncher _gameLauncher;
    private readonly FolderPickerService _folderPicker;
    private readonly AudioService _audioService;
    private ModpackUpdater _modpackUpdater;
    private readonly ApiService _apiService;
    private readonly LauncherUpdateService _updateService;
    private SkinService _skinService;
    private string _minecraftDirectory;

    public MainWindowViewModel()
    {
        _folderPicker = new FolderPickerService();
        _minecraftDirectory = _folderPicker.GetDefaultMinecraftDirectory();

        _authService = new AuthService();
        _installer = new MinecraftInstaller(_minecraftDirectory);
        _gameLauncher = new GameLauncher();
        _audioService = new AudioService();
        _apiService = new ApiService("https://srp-rp-launcher-production.up.railway.app");
        _modpackUpdater = new ModpackUpdater(_minecraftDirectory, _apiService);
        _updateService = new LauncherUpdateService();
        _skinService = new SkinService(_apiService, _minecraftDirectory);

        // РџРѕРґРїРёСЃС‹РІР°РµРјСЃСЏ РЅР° СЃРѕР±С‹С‚РёСЏ
        _installer.StatusChanged += (s, status) => StatusMessage = status;
        _installer.ProgressChanged += (s, progress) => ProgressValue = progress;
        _gameLauncher.OutputReceived += (s, output) => GameOutput += output + "\n";
        _gameLauncher.GameStarted += (s, e) => IsGameRunning = true;
        _gameLauncher.GameExited += (s, code) =>
        {
            // Р’С‹Р·С‹РІР°РµРј РІ UI РїРѕС‚РѕРєРµ С‡С‚РѕР±С‹ РёР·Р±РµР¶Р°С‚СЊ РєСЂР°С€Р°
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsGameRunning = false;
                StatusMessage = $"РРіСЂР° Р·Р°РІРµСЂС€РµРЅР° СЃ РєРѕРґРѕРј: {code}";
            });
        };

        _modpackUpdater.StatusChanged += (s, status) => StatusMessage = status;
        _modpackUpdater.ProgressChanged += (s, progress) => ProgressValue = progress;

        _skinService.StatusChanged += (s, status) => StatusMessage = status;

        // РљРѕРјР°РЅРґС‹
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
        ShowProfileCommand = ReactiveCommand.Create(ShowProfile);
        ResetPasswordCommand = ReactiveCommand.Create(ShowResetPassword);
        ConfirmResetPasswordCommand = ReactiveCommand.CreateFromTask(ConfirmResetPasswordAsync);
        CancelResetCommand = ReactiveCommand.Create(CancelReset);
        EditNicknameCommand = ReactiveCommand.Create(StartEditNickname);
        SaveNicknameCommand = ReactiveCommand.CreateFromTask(SaveNicknameAsync);
        CancelEditNicknameCommand = ReactiveCommand.Create(CancelEditNickname);
        UploadSkinCommand = ReactiveCommand.CreateFromTask(UploadSkinAsync);
        UploadCapeCommand = ReactiveCommand.CreateFromTask(UploadCapeAsync);
        DeleteSkinCommand = ReactiveCommand.CreateFromTask(DeleteSkinAsync);

        // Р—Р°РіСЂСѓР¶Р°РµРј РЅР°СЃС‚СЂРѕР№РєРё RAM
        LoadRamSettings();

        // РђРІС‚РѕРјР°С‚РёС‡РµСЃРєРёР№ РІС…РѕРґ РїСЂРё Р·Р°РїСѓСЃРєРµ
        _ = TryAutoLoginAsync();

        // РџСЂРѕРІРµСЂРєР° РѕР±РЅРѕРІР»РµРЅРёР№ Р»Р°СѓРЅС‡РµСЂР°
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
            Console.WriteLine($"[SaveRamSettings] РћС€РёР±РєР°: {ex.Message}");
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
            Console.WriteLine($"[LoadRamSettings] РћС€РёР±РєР°: {ex.Message}");
        }
    }

    private void SaveToken(string token, string username, string email)
    {
        try
        {
            var data = $"{token}|{username}|{email}";
            var tokenFilePath = GetTokenFilePath();
            File.WriteAllText(tokenFilePath, ProtectLocalData(data));
            Console.WriteLine("[SaveToken] РўРѕРєРµРЅ СЃРѕС…СЂР°РЅРµРЅ");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SaveToken] РћС€РёР±РєР°: {ex.Message}");
        }
    }

    private string ProtectLocalData(string value)
    {
        try
        {
            return SecureStorage.Encrypt(value);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProtectLocalData] Error: {ex.Message}");
            throw;
        }
    }

    private string UnprotectLocalData(string value)
    {
        try
        {
            return SecureStorage.Decrypt(value);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UnprotectLocalData] Error: {ex.Message}");
            // Возвращаем исходное значение для обратной совместимости
            return value;
        }
    }

    private async Task TryAutoLoginAsync()
    {
        try
        {
            var tokenFile = GetTokenFilePath();
            if (!File.Exists(tokenFile))
            {
                Console.WriteLine("[TryAutoLogin] РўРѕРєРµРЅ РЅРµ РЅР°Р№РґРµРЅ");
                return;
            }

            var data = UnprotectLocalData(File.ReadAllText(tokenFile)).Split('|');
            if (data.Length != 3)
            {
                Console.WriteLine("[TryAutoLogin] РќРµРІРµСЂРЅС‹Р№ С„РѕСЂРјР°С‚ С‚РѕРєРµРЅР°");
                return;
            }

            var token = data[0];
            var username = data[1];
            var email = data[2];

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
                    StatusMessage = $"Р”РѕР±СЂРѕ РїРѕР¶Р°Р»РѕРІР°С‚СЊ, {username}!";
                });

                CheckInstallation();
                await CheckModpackVersionAsync();
                await LoadProfileAsync();
                Console.WriteLine("[TryAutoLogin] РђРІС‚РѕРјР°С‚РёС‡РµСЃРєРёР№ РІС…РѕРґ РІС‹РїРѕР»РЅРµРЅ");
            }
            else
            {
                File.Delete(tokenFile);
                Console.WriteLine("[TryAutoLogin] РўРѕРєРµРЅ РЅРµРґРµР№СЃС‚РІРёС‚РµР»РµРЅ, СѓРґР°Р»РµРЅ");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TryAutoLogin] РћС€РёР±РєР°: {ex.Message}");
        }
    }

    private string _username = "Survivor";
    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    private string _userEmail = "";
    public string UserEmail
    {
        get => _userEmail;
        set => this.RaiseAndSetIfChanged(ref _userEmail, value);
    }

    private string _playTimeFormatted = "0 С‡";
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
        ? $"рџџў РћРЅР»Р°Р№РЅ вЂў {PlayersOnline}/{MaxPlayers} РёРіСЂРѕРєРѕРІ"
        : "рџ”ґ РћС„Р»Р°Р№РЅ";

    public string ServerStatusColor => IsServerOnline ? "#53dc96" : "#ff6a4a";

    private string _aboutProjectText = "РРЅС„РѕСЂРјР°С†РёСЏ Рѕ РїСЂРѕРµРєС‚Рµ Р±СѓРґРµС‚ РґРѕР±Р°РІР»РµРЅР° РїРѕР·Р¶Рµ.";
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
        PlayTimeFormatted = hours > 0 ? $"{hours} С‡ {minutes} РјРёРЅ" : $"{minutes} РјРёРЅ";
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
        set => this.RaiseAndSetIfChanged(ref _isClassicSkin, value);
    }

    private bool _isSlimSkin = false;
    public bool IsSlimSkin
    {
        get => _isSlimSkin;
        set => this.RaiseAndSetIfChanged(ref _isSlimSkin, value);
    }

    private string _skinStatus = "РЎРєРёРЅ РЅРµ Р·Р°РіСЂСѓР¶РµРЅ";
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

    private string _statusMessage = "Р”РѕР±СЂРѕ РїРѕР¶Р°Р»РѕРІР°С‚СЊ РІ РїРѕСЃС‚Р°РїРѕРєР°Р»РёРїСЃРёСЃ...";
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

    private string _modpackVersion = "РџСЂРѕРІРµСЂРєР°...";
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

    public ReactiveCommand<Unit, Unit> LoginCommand { get; }
    public ReactiveCommand<Unit, Unit> RegisterCommand { get; }
    public ReactiveCommand<Unit, Unit> InstallCommand { get; }
    public ReactiveCommand<Unit, Unit> LaunchCommand { get; }
    public ReactiveCommand<Unit, Unit> ChooseFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> UpdateModpackCommand { get; }
    public ReactiveCommand<Unit, Unit> UpdateLauncherCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleRegisterCommand { get; }
    public ReactiveCommand<Unit, Unit> LogoutCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetPasswordCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfirmResetPasswordCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelResetCommand { get; }
    public ReactiveCommand<Unit, Unit> EditNicknameCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveNicknameCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelEditNicknameCommand { get; }
    public ReactiveCommand<Unit, Unit> UploadSkinCommand { get; }
    public ReactiveCommand<Unit, Unit> UploadCapeCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteSkinCommand { get; }

    private async Task ChooseFolderAsync()
    {
        // This needs to be called from the View with the Window reference
        // For now, we'll just show a message
        StatusMessage = "РСЃРїРѕР»СЊР·СѓР№С‚Рµ РєРЅРѕРїРєСѓ 'Р’С‹Р±СЂР°С‚СЊ РїР°РїРєСѓ' РІ РёРЅС‚РµСЂС„РµР№СЃРµ";
    }

    public async Task ChooseFolderFromWindowAsync(Window window)
    {
        var folder = await _folderPicker.PickFolderAsync(window, "Р’С‹Р±РµСЂРёС‚Рµ РїР°РїРєСѓ РґР»СЏ СѓСЃС‚Р°РЅРѕРІРєРё Minecraft");

        if (!string.IsNullOrEmpty(folder))
        {
            _minecraftDirectory = folder;
            StatusMessage = $"РџР°РїРєР° СѓСЃС‚Р°РЅРѕРІРєРё: {folder}";

            // РџРµСЂРµСЃРѕР·РґР°РµРј installer СЃ РЅРѕРІРѕР№ РїР°РїРєРѕР№
            _installer = new MinecraftInstaller(_minecraftDirectory);

            // РџРѕРґРїРёСЃС‹РІР°РµРјСЃСЏ РЅР° СЃРѕР±С‹С‚РёСЏ Р·Р°РЅРѕРІРѕ
            _installer.StatusChanged += (s, status) => StatusMessage = status;
            _installer.ProgressChanged += (s, progress) => ProgressValue = progress;

            // РћР±РЅРѕРІР»СЏРµРј ModpackUpdater СЃ РЅРѕРІРѕР№ РїР°РїРєРѕР№
            _modpackUpdater = new ModpackUpdater(_minecraftDirectory, _apiService);
            _modpackUpdater.StatusChanged += (s, status) => StatusMessage = status;
            _modpackUpdater.ProgressChanged += (s, progress) => ProgressValue = progress;

            CheckInstallation();
        }
    }

    private void ToggleRegister()
    {
        IsRegistering = !IsRegistering;
        LoginErrorMessage = null; // РћС‡РёС‰Р°РµРј РѕС€РёР±РєСѓ РїСЂРё РїРµСЂРµРєР»СЋС‡РµРЅРёРё
        StatusMessage = IsRegistering ? "Р РµРіРёСЃС‚СЂР°С†РёСЏ РЅРѕРІРѕРіРѕ Р°РєРєР°СѓРЅС‚Р°" : "Р’С…РѕРґ РІ Р°РєРєР°СѓРЅС‚";
    }

    private void Logout()
    {
        IsLoggedIn = false;
        IsRegistering = false;
        CurrentView = "Login";
        Password = "";
        LoginErrorMessage = null;
        StatusMessage = "Р’С‹ РІС‹С€Р»Рё РёР· Р°РєРєР°СѓРЅС‚Р°";

        // РЈРґР°Р»СЏРµРј СЃРѕС…СЂР°РЅРµРЅРЅС‹Р№ С‚РѕРєРµРЅ
        try
        {
            var tokenFile = GetTokenFilePath();
            if (File.Exists(tokenFile))
            {
                File.Delete(tokenFile);
                Console.WriteLine("[Logout] РўРѕРєРµРЅ СѓРґР°Р»РµРЅ");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Logout] РћС€РёР±РєР° СѓРґР°Р»РµРЅРёСЏ С‚РѕРєРµРЅР°: {ex.Message}");
        }

        Console.WriteLine("[Logout] РџРѕР»СЊР·РѕРІР°С‚РµР»СЊ РІС‹С€РµР» РёР· СЃРёСЃС‚РµРјС‹");
    }

    private void ShowProfile()
    {
        StatusMessage = "Прокрутите вниз чтобы увидеть секцию ‘Персонализация’ со скинами и плащами";
        Console.WriteLine("[ShowProfile] Показана подсказка о секции персонализации");
    }

    private void ShowResetPassword()
    {
        IsResettingPassword = true;
        NewPassword = "";
        RecoveryCode = "";
        LoginErrorMessage = null;
        StatusMessage = "Р’РІРµРґРёС‚Рµ РёРјСЏ РїРѕР»СЊР·РѕРІР°С‚РµР»СЏ, РєРѕРґ РІРѕСЃСЃС‚Р°РЅРѕРІР»РµРЅРёСЏ Рё РЅРѕРІС‹Р№ РїР°СЂРѕР»СЊ";
        Console.WriteLine("[ShowResetPassword] РћС‚РєСЂС‹С‚ СЌРєСЂР°РЅ СЃР±СЂРѕСЃР° РїР°СЂРѕР»СЏ");
    }

    private void CancelReset()
    {
        IsResettingPassword = false;
        RecoveryCode = "";
        NewPassword = "";
        LoginErrorMessage = null;
        StatusMessage = "Р’С…РѕРґ РІ Р°РєРєР°СѓРЅС‚";
        Console.WriteLine("[CancelReset] РћС‚РјРµРЅР° СЃР±СЂРѕСЃР° РїР°СЂРѕР»СЏ");
    }

    private async Task ConfirmResetPasswordAsync()
    {
        try
        {
            Console.WriteLine("[ConfirmResetPasswordAsync] РџРѕРґС‚РІРµСЂР¶РґРµРЅРёРµ СЃР±СЂРѕСЃР°");

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoginErrorMessage = null;
            });

            if (string.IsNullOrWhiteSpace(Username))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Р’РІРµРґРёС‚Рµ РёРјСЏ РїРѕР»СЊР·РѕРІР°С‚РµР»СЏ!";
                });
                return;
            }

            if (string.IsNullOrWhiteSpace(RecoveryCode))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Р’РІРµРґРёС‚Рµ РєРѕРґ РІРѕСЃСЃС‚Р°РЅРѕРІР»РµРЅРёСЏ!";
                });
                return;
            }

            if (string.IsNullOrWhiteSpace(NewPassword))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Р’РІРµРґРёС‚Рµ РЅРѕРІС‹Р№ РїР°СЂРѕР»СЊ!";
                });
                return;
            }

            StatusMessage = "РЎР±СЂРѕСЃ РїР°СЂРѕР»СЏ...";
            var result = await _apiService.ResetPasswordAsync(Username, RecoveryCode, NewPassword);

            if (result.IsSuccess)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsResettingPassword = false;
                    Username = "";
                    RecoveryCode = "";
                    NewPassword = "";
                    LoginErrorMessage = null;
                    StatusMessage = "РџР°СЂРѕР»СЊ РёР·РјРµРЅРµРЅ! Р’РѕР№РґРёС‚Рµ СЃ РЅРѕРІС‹Рј РїР°СЂРѕР»РµРј.";
                });
                Console.WriteLine("[ConfirmResetPasswordAsync] РџР°СЂРѕР»СЊ РёР·РјРµРЅРµРЅ");
            }
            else
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = result.ErrorMessage ?? "РћС€РёР±РєР° СЃР±СЂРѕСЃР° РїР°СЂРѕР»СЏ";
                    StatusMessage = "РћС€РёР±РєР°";
                });
                Console.WriteLine($"[ConfirmResetPasswordAsync] РћС€РёР±РєР°: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoginErrorMessage = $"РћС€РёР±РєР°: {ex.Message}";
                StatusMessage = "РћС€РёР±РєР°";
            });
            Console.WriteLine($"[ConfirmResetPasswordAsync] EXCEPTION: {ex.Message}");
        }
    }

    private async Task RegisterAsync()
    {
        try
        {
            Console.WriteLine("[RegisterAsync] РќР°С‡Р°Р»Рѕ СЂРµРіРёСЃС‚СЂР°С†РёРё");

            // РћС‡РёС‰Р°РµРј РїСЂРµРґС‹РґСѓС‰РёРµ РѕС€РёР±РєРё РІ UI РїРѕС‚РѕРєРµ
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoginErrorMessage = null;
            });

            if (string.IsNullOrWhiteSpace(Username))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Р’РІРµРґРёС‚Рµ РёРјСЏ РїРѕР»СЊР·РѕРІР°С‚РµР»СЏ!";
                });
                Console.WriteLine("[RegisterAsync] РћС€РёР±РєР°: РїСѓСЃС‚РѕРµ РёРјСЏ");
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Р’РІРµРґРёС‚Рµ РїР°СЂРѕР»СЊ!";
                });
                Console.WriteLine("[RegisterAsync] РћС€РёР±РєР°: РїСѓСЃС‚РѕР№ РїР°СЂРѕР»СЊ");
                return;
            }

            StatusMessage = "Р РµРіРёСЃС‚СЂР°С†РёСЏ...";
            Console.WriteLine($"[RegisterAsync] РћС‚РїСЂР°РІРєР° Р·Р°РїСЂРѕСЃР°: {Username}");

            var result = await _apiService.RegisterAsync(Username, Password);

            Console.WriteLine($"[RegisterAsync] Р РµР·СѓР»СЊС‚Р°С‚: Success={result.IsSuccess}, Error={result.ErrorMessage}");

            if (result.IsSuccess && result.Data != null)
            {
                // Р’РђР–РќРћ: РџРѕРєР°Р·С‹РІР°РµРј recovery code РїРѕР»СЊР·РѕРІР°С‚РµР»СЋ
                var recoveryCode = result.Data.RecoveryCode ?? "";

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // РќР• РІС…РѕРґРёРј СЃСЂР°Р·Сѓ - РїРѕРєР°Р·С‹РІР°РµРј РєРѕРґ РЅР° СЌРєСЂР°РЅРµ РІС…РѕРґР°
                    IsLoggedIn = false;
                    IsRegistering = false;
                    CurrentView = "Login";
                    Username = "";
                    Password = "";

                    // РџРѕРєР°Р·С‹РІР°РµРј recovery code РІ РѕС‚РґРµР»СЊРЅРѕРј РєРѕРїРёСЂСѓРµРјРѕРј РїРѕР»Рµ
                    RecoveryCodeDisplay = recoveryCode;
                    ShowRecoveryCode = true;

                    LoginErrorMessage = $"вњ… Р Р•Р“РРЎРўР РђР¦РРЇ РЈРЎРџР•РЁРќРђ!\n\nвљ пёЏ РЎРћРҐР РђРќРРўР• РљРћР” Р’РћРЎРЎРўРђРќРћР’Р›Р•РќРРЇ РќРР–Р•!\nР’С‹РґРµР»РёС‚Рµ Рё СЃРєРѕРїРёСЂСѓР№С‚Рµ РµРіРѕ (Ctrl+C).\nРћРЅ РїРѕРЅР°РґРѕР±РёС‚СЃСЏ РґР»СЏ РІРѕСЃСЃС‚Р°РЅРѕРІР»РµРЅРёСЏ РїР°СЂРѕР»СЏ.\nРљРѕРґ Р±РѕР»СЊС€Рµ РЅРµ Р±СѓРґРµС‚ РїРѕРєР°Р·Р°РЅ!";
                    StatusMessage = $"Р РµРіРёСЃС‚СЂР°С†РёСЏ Р·Р°РІРµСЂС€РµРЅР°. РЎРѕС…СЂР°РЅРёС‚Рµ РєРѕРґ Рё РІРѕР№РґРёС‚Рµ.";
                });

                Console.WriteLine($"[RegisterAsync] РџРѕР»СЊР·РѕРІР°С‚РµР»СЊ РґРѕР»Р¶РµРЅ СЃРѕС…СЂР°РЅРёС‚СЊ РєРѕРґ Рё РІРѕР№С‚Рё Р·Р°РЅРѕРІРѕ");
            }
            else
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = result.ErrorMessage ?? "РћС€РёР±РєР° СЂРµРіРёСЃС‚СЂР°С†РёРё";
                    StatusMessage = "РћС€РёР±РєР° СЂРµРіРёСЃС‚СЂР°С†РёРё";
                });
                Console.WriteLine($"[RegisterAsync] РћС€РёР±РєР°: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoginErrorMessage = $"РћС€РёР±РєР° РїРѕРґРєР»СЋС‡РµРЅРёСЏ Рє СЃРµСЂРІРµСЂСѓ";
                StatusMessage = "РћС€РёР±РєР° РїРѕРґРєР»СЋС‡РµРЅРёСЏ";
            });
            Console.WriteLine($"[RegisterAsync] EXCEPTION: {ex.Message}");
            Console.WriteLine($"[RegisterAsync] Stack: {ex.StackTrace}");
        }
    }

    private async Task LoginAsync()
    {
        try
        {
            Console.WriteLine("[LoginAsync] РќР°С‡Р°Р»Рѕ РІС…РѕРґР°");

            // РћС‡РёС‰Р°РµРј РїСЂРµРґС‹РґСѓС‰РёРµ РѕС€РёР±РєРё Рё СЃРєСЂС‹РІР°РµРј recovery code
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoginErrorMessage = null;
                ShowRecoveryCode = false;
                RecoveryCodeDisplay = "";
            });

            if (string.IsNullOrWhiteSpace(Username))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Р’РІРµРґРёС‚Рµ РёРјСЏ РїРѕР»СЊР·РѕРІР°С‚РµР»СЏ!";
                });
                Console.WriteLine("[LoginAsync] РћС€РёР±РєР°: РїСѓСЃС‚РѕРµ РёРјСЏ");
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Р’РІРµРґРёС‚Рµ РїР°СЂРѕР»СЊ!";
                });
                Console.WriteLine("[LoginAsync] РћС€РёР±РєР°: РїСѓСЃС‚РѕР№ РїР°СЂРѕР»СЊ");
                return;
            }

            StatusMessage = "Р’С…РѕРґ...";
            Console.WriteLine($"[LoginAsync] РћС‚РїСЂР°РІРєР° Р·Р°РїСЂРѕСЃР°: {Username}");

            var result = await _apiService.LoginAsync(Username, Password);

            Console.WriteLine($"[LoginAsync] Р РµР·СѓР»СЊС‚Р°С‚: Success={result.IsSuccess}, Error={result.ErrorMessage}");

            if (result.IsSuccess && result.Data != null)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLoggedIn = true;
                    CurrentView = "Main";
                    StatusMessage = $"Р”РѕР±СЂРѕ РїРѕР¶Р°Р»РѕРІР°С‚СЊ, {result.Data.Username}!";
                    Username = result.Data.Username;
                    UserEmail = result.Data.Email;
                    LoginErrorMessage = null;
                });

                // РЎРѕС…СЂР°РЅСЏРµРј С‚РѕРєРµРЅ РґР»СЏ Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРѕРіРѕ РІС…РѕРґР°
                SaveToken(result.Data.Token ?? "", result.Data.Username, result.Data.Email);

                Console.WriteLine("[LoginAsync] Р’С…РѕРґ СѓСЃРїРµС€РµРЅ!");

                // РџСЂРѕРІРµСЂСЏРµРј СѓСЃС‚Р°РЅРѕРІРєСѓ
                CheckInstallation();
                await CheckModpackVersionAsync();
                await LoadProfileAsync();
                await LoadServerStatusAsync();
            }
            else
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = result.ErrorMessage ?? "РќРµРІРµСЂРЅРѕРµ РёРјСЏ РїРѕР»СЊР·РѕРІР°С‚РµР»СЏ РёР»Рё РїР°СЂРѕР»СЊ";
                    StatusMessage = "РћС€РёР±РєР° РІС…РѕРґР°";
                });
                Console.WriteLine($"[LoginAsync] РћС€РёР±РєР°: {result.ErrorMessage}");
                Console.WriteLine($"[LoginAsync] LoginErrorMessage СѓСЃС‚Р°РЅРѕРІР»РµРЅ: {LoginErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoginErrorMessage = $"РћС€РёР±РєР° РїРѕРґРєР»СЋС‡РµРЅРёСЏ Рє СЃРµСЂРІРµСЂСѓ";
                StatusMessage = "РћС€РёР±РєР° РїРѕРґРєР»СЋС‡РµРЅРёСЏ";
            });
            Console.WriteLine($"[LoginAsync] EXCEPTION: {ex.Message}");
            Console.WriteLine($"[LoginAsync] Stack: {ex.StackTrace}");
        }
    }

    private async Task LoginAsync_Old()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                StatusMessage = "Р’РІРµРґРёС‚Рµ РёРјСЏ РІС‹Р¶РёРІС€РµРіРѕ!";
                return;
            }

            StatusMessage = "РђСѓС‚РµРЅС‚РёС„РёРєР°С†РёСЏ...";
            var authResult = _authService.AuthenticateOffline(Username);

            IsLoggedIn = true;
            CurrentView = "Main";
            StatusMessage = $"Р”РѕР±СЂРѕ РїРѕР¶Р°Р»РѕРІР°С‚СЊ, {authResult.Username}!";

            // РџСЂРѕРІРµСЂСЏРµРј СѓСЃС‚Р°РЅРѕРІРєСѓ
            CheckInstallation();

            // РџСЂРѕРІРµСЂСЏРµРј РІРµСЂСЃРёСЋ СЃР±РѕСЂРєРё
            await CheckModpackVersionAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"РћС€РёР±РєР° РІС…РѕРґР°: {ex.Message}";
        }
    }

    private async Task CheckModpackVersionAsync()
    {
        try
        {
            var currentVersion = await _modpackUpdater.GetCurrentVersionAsync();
            ModpackVersion = $"РЎР±РѕСЂРєР°: v{currentVersion}";

            // РџСЂРѕРІРµСЂСЏРµРј РЅР°Р»РёС‡РёРµ РѕР±РЅРѕРІР»РµРЅРёР№
            var hasUpdate = await _modpackUpdater.CheckForUpdatesAsync();
            if (hasUpdate)
            {
                StatusMessage = "Р”РѕСЃС‚СѓРїРЅРѕ РѕР±РЅРѕРІР»РµРЅРёРµ СЃР±РѕСЂРєРё!";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CheckModpackVersion] Error: {ex.Message}");
            ModpackVersion = "РЎР±РѕСЂРєР°: РЅРµ СѓСЃС‚Р°РЅРѕРІР»РµРЅР°";
        }
    }

    private async Task UpdateModpackAsync()
    {
        try
        {
            StatusMessage = "РџСЂРѕРІРµСЂРєР° РѕР±РЅРѕРІР»РµРЅРёР№ СЃР±РѕСЂРєРё...";
            ProgressValue = 0;

            var hasUpdate = await _modpackUpdater.CheckForUpdatesAsync();

            if (!hasUpdate)
            {
                StatusMessage = "РЈ РІР°СЃ СѓСЃС‚Р°РЅРѕРІР»РµРЅР° РїРѕСЃР»РµРґРЅСЏСЏ РІРµСЂСЃРёСЏ СЃР±РѕСЂРєРё";
                return;
            }

            var success = await _modpackUpdater.DownloadAndInstallModpackAsync();

            if (success)
            {
                StatusMessage = "РЎР±РѕСЂРєР° СѓСЃРїРµС€РЅРѕ РѕР±РЅРѕРІР»РµРЅР°!";
                await CheckModpackVersionAsync();
            }
            else
            {
                StatusMessage = "РћС€РёР±РєР° РѕР±РЅРѕРІР»РµРЅРёСЏ СЃР±РѕСЂРєРё";
            }

            ProgressValue = 0;
        }
        catch (Exception ex)
        {
            StatusMessage = $"РћС€РёР±РєР° РѕР±РЅРѕРІР»РµРЅРёСЏ: {ex.Message}";
            Console.WriteLine($"[UpdateModpackAsync] ERROR: {ex.Message}");
            ProgressValue = 0;
        }
    }

    private async Task InstallMinecraftAsync()
    {
        try
        {
            Console.WriteLine($"[InstallMinecraftAsync] Starting installation to: {_minecraftDirectory}");
            StatusMessage = "РќР°С‡РёРЅР°РµРј СѓСЃС‚Р°РЅРѕРІРєСѓ...";
            ProgressValue = 0;

            Console.WriteLine("[InstallMinecraftAsync] Calling InstallMinecraftAsync...");
            var success = await _installer.InstallMinecraftAsync();

            if (success)
            {
                Console.WriteLine("[InstallMinecraftAsync] Minecraft installed, installing Forge...");
                StatusMessage = "РЈСЃС‚Р°РЅРѕРІРєР° Forge...";

                bool forgeSuccess = false;
                try
                {
                    forgeSuccess = await _installer.InstallForgeAsync();
                    if (forgeSuccess)
                    {
                        Console.WriteLine("[InstallMinecraftAsync] Forge installed successfully!");
                        StatusMessage = "Forge СѓСЃС‚Р°РЅРѕРІР»РµРЅ!";
                    }
                    else
                    {
                        Console.WriteLine("[InstallMinecraftAsync] Forge installation failed!");
                        StatusMessage = "РћС€РёР±РєР° СѓСЃС‚Р°РЅРѕРІРєРё Forge. РРіСЂР° Р±СѓРґРµС‚ Р·Р°РїСѓС‰РµРЅР° РІ vanilla СЂРµР¶РёРјРµ.";
                    }
                }
                catch (Exception forgeEx)
                {
                    Console.WriteLine($"[InstallMinecraftAsync] Forge installation exception: {forgeEx.Message}");
                    Console.WriteLine($"Stack trace: {forgeEx.StackTrace}");
                    StatusMessage = $"РћС€РёР±РєР° Forge: {forgeEx.Message}. РРіСЂР° Р±СѓРґРµС‚ РІ vanilla СЂРµР¶РёРјРµ.";
                }

                // РЎРєР°С‡РёРІР°РµРј РјРѕРґРїР°Рє РїРѕСЃР»Рµ СѓСЃС‚Р°РЅРѕРІРєРё Forge
                if (forgeSuccess)
                {
                    Console.WriteLine("[InstallMinecraftAsync] Downloading modpack...");
                    StatusMessage = "РЎРєР°С‡РёРІР°РЅРёРµ СЃР±РѕСЂРєРё РјРѕРґРѕРІ...";
                    ProgressValue = 0;

                    try
                    {
                        var modpackSuccess = await _modpackUpdater.DownloadAndInstallModpackAsync();
                        if (modpackSuccess)
                        {
                            Console.WriteLine("[InstallMinecraftAsync] Modpack installed successfully!");
                            StatusMessage = "РЈСЃС‚Р°РЅРѕРІРєР° Р·Р°РІРµСЂС€РµРЅР°! Р“РѕС‚РѕРІ Рє Р·Р°РїСѓСЃРєСѓ.";
                            await CheckModpackVersionAsync();
                        }
                        else
                        {
                            Console.WriteLine("[InstallMinecraftAsync] Modpack installation failed!");
                            StatusMessage = "РћС€РёР±РєР° СѓСЃС‚Р°РЅРѕРІРєРё СЃР±РѕСЂРєРё. РСЃРїРѕР»СЊР·СѓР№С‚Рµ РєРЅРѕРїРєСѓ 'РћР±РЅРѕРІРёС‚СЊ СЃР±РѕСЂРєСѓ'.";
                        }
                    }
                    catch (Exception modpackEx)
                    {
                        Console.WriteLine($"[InstallMinecraftAsync] Modpack installation exception: {modpackEx.Message}");
                        Console.WriteLine($"Stack trace: {modpackEx.StackTrace}");
                        StatusMessage = $"РћС€РёР±РєР° СЃР±РѕСЂРєРё: {modpackEx.Message}. РСЃРїРѕР»СЊР·СѓР№С‚Рµ РєРЅРѕРїРєСѓ 'РћР±РЅРѕРІРёС‚СЊ СЃР±РѕСЂРєСѓ'.";
                    }
                }
                else
                {
                    StatusMessage = "Forge РЅРµ СѓСЃС‚Р°РЅРѕРІР»РµРЅ. РЎР±РѕСЂРєР° РЅРµ Р±СѓРґРµС‚ Р·Р°РіСЂСѓР¶РµРЅР°.";
                }

                IsInstalled = true;
                Console.WriteLine("[InstallMinecraftAsync] Installation complete!");
            }
            else
            {
                StatusMessage = "РћС€РёР±РєР° СѓСЃС‚Р°РЅРѕРІРєРё. РџСЂРѕРІРµСЂСЊС‚Рµ РїРѕРґРєР»СЋС‡РµРЅРёРµ Рє СЃРµС‚Рё.";
                Console.WriteLine("[InstallMinecraftAsync] Installation failed!");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"РљСЂРёС‚РёС‡РµСЃРєР°СЏ РѕС€РёР±РєР°: {ex.Message}";
            Console.WriteLine($"[InstallMinecraftAsync] EXCEPTION: {ex.Message}");
            Console.WriteLine($"[InstallMinecraftAsync] Stack trace: {ex.StackTrace}");
        }
    }

    private async Task LaunchGameAsync()
    {
        try
        {
            StatusMessage = "РџРѕРґРіРѕС‚РѕРІРєР° Рє Р·Р°РїСѓСЃРєСѓ...";
            GameOutput = string.Empty;

            var authResult = _authService.AuthenticateOffline(Username);

            // CreateLaunchOptions Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРё РѕРїСЂРµРґРµР»РёС‚ Forge РёР»Рё vanilla
            var launchOptions = _installer.CreateLaunchOptions(authResult);

            // РЈСЃС‚Р°РЅР°РІР»РёРІР°РµРј РїРѕР»РЅРѕСЌРєСЂР°РЅРЅС‹Р№ СЂРµР¶РёРј
            launchOptions.IsFullscreen = IsFullscreen;

            // РЈСЃС‚Р°РЅР°РІР»РёРІР°РµРј РІС‹РґРµР»РµРЅРЅСѓСЋ RAM
            launchOptions.MaxMemory = _allocatedRamGB * 1024;
            launchOptions.MinMemory = Math.Min(512, _allocatedRamGB * 512);

            // РР·РІР»РµРєР°РµРј РЅР°С‚РёРІРЅС‹Рµ Р±РёР±Р»РёРѕС‚РµРєРё РїРµСЂРµРґ Р·Р°РїСѓСЃРєРѕРј
            StatusMessage = "РР·РІР»РµС‡РµРЅРёРµ РЅР°С‚РёРІРЅС‹С… Р±РёР±Р»РёРѕС‚РµРє...";
            _installer.ExtractNatives(launchOptions.Version);

            StatusMessage = "Р—Р°РїСѓСЃРє РёРіСЂС‹...";
            _gameLauncher.LaunchGame(launchOptions);

            StatusMessage = launchOptions.Version.Contains("forge") ? "Forge Р·Р°РїСѓС‰РµРЅ! Р’С‹Р¶РёРІР°Р№С‚Рµ..." : "РРіСЂР° Р·Р°РїСѓС‰РµРЅР°! Р’С‹Р¶РёРІР°Р№С‚Рµ...";
        }
        catch (Exception ex)
        {
            StatusMessage = $"РћС€РёР±РєР° Р·Р°РїСѓСЃРєР°: {ex.Message}";
            Console.WriteLine($"[LaunchGameAsync] ERROR: {ex.Message}");
            Console.WriteLine($"[LaunchGameAsync] Stack trace: {ex.StackTrace}");
            IsGameRunning = false;
        }
    }

    private void CheckInstallation()
    {
        // РџСЂРѕРІРµСЂСЏРµРј Forge РІРµСЂСЃРёСЋ (РїСЂРёРѕСЂРёС‚РµС‚)
        var forgeJsonPath = Path.Combine(_minecraftDirectory, "versions", "1.20.1-forge-47.3.0", "1.20.1-forge-47.3.0.json");
        var vanillaJarPath = Path.Combine(_minecraftDirectory, "versions", "1.20.1", "1.20.1.jar");

        bool forgeInstalled = File.Exists(forgeJsonPath);
        bool vanillaInstalled = File.Exists(vanillaJarPath);

        IsInstalled = vanillaInstalled; // Р”Р»СЏ Р·Р°РїСѓСЃРєР° РЅСѓР¶РЅР° С…РѕС‚СЏ Р±С‹ vanilla

        if (forgeInstalled && vanillaInstalled)
            StatusMessage = "Minecraft 1.20.1 Forge СѓСЃС‚Р°РЅРѕРІР»РµРЅ. Р“РѕС‚РѕРІ Рє Р·Р°РїСѓСЃРєСѓ.";
        else if (vanillaInstalled)
            StatusMessage = "Minecraft СѓСЃС‚Р°РЅРѕРІР»РµРЅ. РќР°Р¶РјРёС‚Рµ 'РџСЂРѕРІРµСЂРёС‚СЊ С„Р°Р№Р»С‹' РґР»СЏ СѓСЃС‚Р°РЅРѕРІРєРё Forge.";
        else
            StatusMessage = "РўСЂРµР±СѓРµС‚СЃСЏ СѓСЃС‚Р°РЅРѕРІРєР° Minecraft 1.20.1 Forge";
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
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LoadProfile] РћС€РёР±РєР°: {ex.Message}");
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
            Console.WriteLine($"[LoadServerStatus] РћС€РёР±РєР°: {ex.Message}");
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
                LoginErrorMessage = "Р’РІРµРґРёС‚Рµ РЅРѕРІС‹Р№ РЅРёРєРЅРµР№Рј";
                return;
            }

            if (NewNickname.Length < 3 || NewNickname.Length > 16)
            {
                LoginErrorMessage = "РќРёРєРЅРµР№Рј РґРѕР»Р¶РµРЅ Р±С‹С‚СЊ РѕС‚ 3 РґРѕ 16 СЃРёРјРІРѕР»РѕРІ";
                return;
            }

            StatusMessage = "РР·РјРµРЅРµРЅРёРµ РЅРёРєРЅРµР№РјР°...";
            var result = await _apiService.ChangeUsernameAsync(NewNickname);

            if (result.IsSuccess)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Username = NewNickname;
                    IsEditingNickname = false;
                    NewNickname = "";
                    LoginErrorMessage = null;
                    StatusMessage = "РќРёРєРЅРµР№Рј СѓСЃРїРµС€РЅРѕ РёР·РјРµРЅРµРЅ!";
                });

                // РћР±РЅРѕРІР»СЏРµРј СЃРѕС…СЂР°РЅРµРЅРЅС‹Р№ С‚РѕРєРµРЅ СЃ РЅРѕРІС‹Рј РЅРёРєРЅРµР№РјРѕРј
                var tokenFile = GetTokenFilePath();
                if (File.Exists(tokenFile))
                {
                    var data = UnprotectLocalData(File.ReadAllText(tokenFile)).Split('|');
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
                    LoginErrorMessage = result.ErrorMessage ?? "РћС€РёР±РєР° СЃРјРµРЅС‹ РЅРёРєРЅРµР№РјР°";
                    StatusMessage = "РћС€РёР±РєР° СЃРјРµРЅС‹ РЅРёРєРЅРµР№РјР°";
                });
            }
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoginErrorMessage = $"РћС€РёР±РєР°: {ex.Message}";
                StatusMessage = "РћС€РёР±РєР° СЃРјРµРЅС‹ РЅРёРєРЅРµР№РјР°";
            });
        }
    }

    private async Task CheckForLauncherUpdatesAsync()
    {
        try
        {
            await Task.Delay(2000); // РќРµР±РѕР»СЊС€Р°СЏ Р·Р°РґРµСЂР¶РєР° РїРѕСЃР»Рµ Р·Р°РїСѓСЃРєР°

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
            Console.WriteLine($"[CheckForLauncherUpdatesAsync] РћС€РёР±РєР°: {ex.Message}");
        }
    }

    private async Task UpdateLauncherAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_launcherUpdateUrl))
            {
                StatusMessage = "РћС€РёР±РєР°: URL РѕР±РЅРѕРІР»РµРЅРёСЏ РЅРµ РЅР°Р№РґРµРЅ";
                return;
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = "РћР±РЅРѕРІР»РµРЅРёРµ Р»Р°СѓРЅС‡РµСЂР°...";
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
                StatusMessage = $"РћС€РёР±РєР° РѕР±РЅРѕРІР»РµРЅРёСЏ: {ex.Message}";
            });
            Console.WriteLine($"[UpdateLauncherAsync] РћС€РёР±РєР°: {ex.Message}");
        }
    }

    // РњРµС‚РѕРґС‹ РґР»СЏ СЂР°Р±РѕС‚С‹ СЃРѕ СЃРєРёРЅР°РјРё
    private async Task UploadSkinAsync()
    {
        try
        {
            SkinStatus = "Р’С‹Р±РµСЂРёС‚Рµ PNG С„Р°Р№Р» СЃРєРёРЅР° 64x64 РїРёРєСЃРµР»РµР№";

            // РћС‚РєСЂС‹РІР°РµРј РґРёР°Р»РѕРі РІС‹Р±РѕСЂР° С„Р°Р№Р»Р°
            var dialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Р’С‹Р±РµСЂРёС‚Рµ С„Р°Р№Р» СЃРєРёРЅР° (PNG 64x64)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("PNG РёР·РѕР±СЂР°Р¶РµРЅРёСЏ")
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
                SkinStatus = "РћС€РёР±РєР°: РЅРµ СѓРґР°Р»РѕСЃСЊ РїРѕР»СѓС‡РёС‚СЊ РѕРєРЅРѕ";
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(dialog);

            if (files.Count == 0)
            {
                SkinStatus = "Р’С‹Р±РѕСЂ С„Р°Р№Р»Р° РѕС‚РјРµРЅРµРЅ";
                return;
            }

            var filePath = files[0].Path.LocalPath;

            // Р’Р°Р»РёРґР°С†РёСЏ С„Р°Р№Р»Р°
            if (!_skinService.ValidateSkinFile(filePath, out var error))
            {
                SkinStatus = $"РћС€РёР±РєР°: {error}";
                return;
            }

            SkinStatus = "Р—Р°РіСЂСѓР·РєР° СЃРєРёРЅР° РЅР° СЃРµСЂРІРµСЂ...";

            var skinType = IsClassicSkin ? "classic" : "slim";
            var success = await _skinService.UploadSkinAsync(filePath, skinType);

            if (success)
            {
                SkinStatus = "РЎРєРёРЅ СѓСЃРїРµС€РЅРѕ Р·Р°РіСЂСѓР¶РµРЅ!";
                await LoadSkinPreviewAsync(filePath);
            }
            else
            {
                SkinStatus = "РћС€РёР±РєР° Р·Р°РіСЂСѓР·РєРё СЃРєРёРЅР°";
            }
        }
        catch (Exception ex)
        {
            SkinStatus = $"РћС€РёР±РєР°: {ex.Message}";
            Console.WriteLine($"[UploadSkinAsync] РћС€РёР±РєР°: {ex.Message}");
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
                SkinStatus = $"РЎРєРёРЅ Р·Р°РіСЂСѓР¶РµРЅ ({skinType})";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"РћС€РёР±РєР° Р·Р°РіСЂСѓР·РєРё СЃРєРёРЅР°: {ex.Message}";
            Console.WriteLine($"[UploadSkinFromFileAsync] РћС€РёР±РєР°: {ex.Message}");
        }
    }

    private async Task UploadCapeAsync()
    {
        try
        {
            SkinStatus = "Р’С‹Р±РµСЂРёС‚Рµ PNG С„Р°Р№Р» РїР»Р°С‰Р° 64x32 РїРёРєСЃРµР»РµР№";

            var dialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Р’С‹Р±РµСЂРёС‚Рµ С„Р°Р№Р» РїР»Р°С‰Р° (PNG 64x32)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("PNG РёР·РѕР±СЂР°Р¶РµРЅРёСЏ")
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
                SkinStatus = "РћС€РёР±РєР°: РЅРµ СѓРґР°Р»РѕСЃСЊ РїРѕР»СѓС‡РёС‚СЊ РѕРєРЅРѕ";
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(dialog);

            if (files.Count == 0)
            {
                SkinStatus = "Р’С‹Р±РѕСЂ С„Р°Р№Р»Р° РѕС‚РјРµРЅРµРЅ";
                return;
            }

            var filePath = files[0].Path.LocalPath;

            if (!_skinService.ValidateCapeFile(filePath, out var error))
            {
                SkinStatus = $"РћС€РёР±РєР°: {error}";
                return;
            }

            SkinStatus = "Р—Р°РіСЂСѓР·РєР° РїР»Р°С‰Р° РЅР° СЃРµСЂРІРµСЂ...";

            var success = await _skinService.UploadCapeAsync(filePath);

            if (success)
            {
                SkinStatus = "РџР»Р°С‰ СѓСЃРїРµС€РЅРѕ Р·Р°РіСЂСѓР¶РµРЅ!";
            }
            else
            {
                SkinStatus = "РћС€РёР±РєР° Р·Р°РіСЂСѓР·РєРё РїР»Р°С‰Р°";
            }
        }
        catch (Exception ex)
        {
            SkinStatus = $"РћС€РёР±РєР°: {ex.Message}";
            Console.WriteLine($"[UploadCapeAsync] РћС€РёР±РєР°: {ex.Message}");
        }
    }

    public async Task UploadCapeFromFileAsync(string filePath)
    {
        try
        {
            var success = await _skinService.UploadCapeAsync(filePath);

            if (success)
            {
                SkinStatus = "РџР»Р°С‰ Р·Р°РіСЂСѓР¶РµРЅ";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"РћС€РёР±РєР° Р·Р°РіСЂСѓР·РєРё РїР»Р°С‰Р°: {ex.Message}";
            Console.WriteLine($"[UploadCapeFromFileAsync] РћС€РёР±РєР°: {ex.Message}");
        }
    }

    private async Task DeleteSkinAsync()
    {
        try
        {
            SkinStatus = "РЈРґР°Р»РµРЅРёРµ СЃРєРёРЅР°...";

            var success = await _skinService.DeleteCurrentSkinAsync();

            if (success)
            {
                SkinStatus = "РЎРєРёРЅ СѓРґР°Р»РµРЅ";
                CurrentSkinPreview = null;
            }
            else
            {
                SkinStatus = "РћС€РёР±РєР° СѓРґР°Р»РµРЅРёСЏ СЃРєРёРЅР°";
            }
        }
        catch (Exception ex)
        {
            SkinStatus = $"РћС€РёР±РєР° СѓРґР°Р»РµРЅРёСЏ СЃРєРёРЅР°: {ex.Message}";
            Console.WriteLine($"[DeleteSkinAsync] РћС€РёР±РєР°: {ex.Message}");
        }
    }

    private async Task LoadSkinPreviewAsync(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            CurrentSkinPreview = new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LoadSkinPreviewAsync] РћС€РёР±РєР° Р·Р°РіСЂСѓР·РєРё РїСЂРµРІСЊСЋ: {ex.Message}");
        }
    }
}

