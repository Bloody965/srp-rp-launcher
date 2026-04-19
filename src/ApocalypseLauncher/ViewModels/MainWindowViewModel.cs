using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive;
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
        _gameLauncher.OutputReceived += (s, output) => GameOutput += output + "\n";
        _gameLauncher.GameStarted += (s, e) => IsGameRunning = true;
        _gameLauncher.GameExited += (s, code) =>
        {
            // Вызываем в UI потоке чтобы избежать краша
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsGameRunning = false;
                StatusMessage = $"Игра завершена с кодом: {code}";
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
        SendResetCodeCommand = ReactiveCommand.CreateFromTask(SendResetCodeAsync);
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
        ShowProfileTabCommand = ReactiveCommand.Create(() => { CurrentTab = "Profile"; });

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

    private void SaveToken(string token, string username, string email)
    {
        try
        {
            var data = $"{token}|{username}|{email}";
            File.WriteAllText(GetTokenFilePath(), data);
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

            var data = File.ReadAllText(tokenFile).Split('|');
            if (data.Length != 3)
            {
                Console.WriteLine("[TryAutoLogin] Неверный формат токена");
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
                    StatusMessage = $"Добро пожаловать, {username}!";
                });

                CheckInstallation();
                await CheckModpackVersionAsync();
                await LoadProfileAsync();
                await LoadCurrentSkinAsync();
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

    private bool _resetCodeSent = false;
    public bool ResetCodeSent
    {
        get => _resetCodeSent;
        set
        {
            this.RaiseAndSetIfChanged(ref _resetCodeSent, value);
            this.RaisePropertyChanged(nameof(ResetPasswordDescription));
        }
    }

    public string ResetPasswordDescription => _resetCodeSent
        ? "Код отправлен на вашу почту. Введите его ниже."
        : "Укажите email для получения кода восстановления.";

    private string _resetCode = "";
    public string ResetCode
    {
        get => _resetCode;
        set => this.RaiseAndSetIfChanged(ref _resetCode, value);
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

    public ReactiveCommand<Unit, Unit> ShowHomeTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowPersonalizationTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowProfileTabCommand { get; }

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
    public ReactiveCommand<Unit, Unit> SendResetCodeCommand { get; }
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

    private void ToggleRegister()
    {
        IsRegistering = !IsRegistering;
        LoginErrorMessage = null; // Очищаем ошибку при переключении
        StatusMessage = IsRegistering ? "Регистрация нового аккаунта" : "Вход в аккаунт";
    }

    private void Logout()
    {
        IsLoggedIn = false;
        IsRegistering = false;
        CurrentView = "Login";
        Password = "";
        LoginErrorMessage = null;
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

    private void ShowResetPassword()
    {
        IsResettingPassword = true;
        ResetCodeSent = false;
        Email = "";
        ResetCode = "";
        NewPassword = "";
        LoginErrorMessage = null;
        StatusMessage = "Введите email для сброса пароля";
        Console.WriteLine("[ShowResetPassword] Открыт экран сброса пароля");
    }

    private void CancelReset()
    {
        IsResettingPassword = false;
        ResetCodeSent = false;
        Email = "";
        ResetCode = "";
        NewPassword = "";
        LoginErrorMessage = null;
        StatusMessage = "Вход в аккаунт";
        Console.WriteLine("[CancelReset] Отмена сброса пароля");
    }

    private async Task SendResetCodeAsync()
    {
        try
        {
            Console.WriteLine("[SendResetCodeAsync] Отправка кода");

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoginErrorMessage = null;
            });

            if (string.IsNullOrWhiteSpace(Email))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Введите email!";
                });
                return;
            }

            StatusMessage = "Отправка кода на почту...";
            var result = await _apiService.RequestResetCodeAsync(Email);

            if (result.IsSuccess)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ResetCodeSent = true;
                    LoginErrorMessage = null;
                    StatusMessage = "Код отправлен! Проверьте почту.";
                });
                Console.WriteLine("[SendResetCodeAsync] Код отправлен");
            }
            else
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = result.ErrorMessage ?? "Ошибка отправки кода";
                    StatusMessage = "Ошибка";
                });
                Console.WriteLine($"[SendResetCodeAsync] Ошибка: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoginErrorMessage = $"Ошибка: {ex.Message}";
                StatusMessage = "Ошибка";
            });
            Console.WriteLine($"[SendResetCodeAsync] EXCEPTION: {ex.Message}");
        }
    }

    private async Task ConfirmResetPasswordAsync()
    {
        try
        {
            Console.WriteLine("[ConfirmResetPasswordAsync] Подтверждение сброса");

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoginErrorMessage = null;
            });

            if (string.IsNullOrWhiteSpace(Username))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Введите имя пользователя!";
                });
                return;
            }

            if (string.IsNullOrWhiteSpace(RecoveryCode))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Введите код восстановления!";
                });
                return;
            }

            if (string.IsNullOrWhiteSpace(NewPassword))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Введите новый пароль!";
                });
                return;
            }

            StatusMessage = "Сброс пароля...";
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
                    StatusMessage = "Пароль изменен! Войдите с новым паролем.";
                });
                Console.WriteLine("[ConfirmResetPasswordAsync] Пароль изменен");
            }
            else
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = result.ErrorMessage ?? "Ошибка сброса пароля";
                    StatusMessage = "Ошибка";
                });
                Console.WriteLine($"[ConfirmResetPasswordAsync] Ошибка: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoginErrorMessage = $"Ошибка: {ex.Message}";
                StatusMessage = "Ошибка";
            });
            Console.WriteLine($"[ConfirmResetPasswordAsync] EXCEPTION: {ex.Message}");
        }
    }

    private async Task RegisterAsync()
    {
        try
        {
            Console.WriteLine("[RegisterAsync] Начало регистрации");

            // Очищаем предыдущие ошибки в UI потоке
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoginErrorMessage = null;
            });

            if (string.IsNullOrWhiteSpace(Username))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Введите имя пользователя!";
                });
                Console.WriteLine("[RegisterAsync] Ошибка: пустое имя");
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Введите пароль!";
                });
                Console.WriteLine("[RegisterAsync] Ошибка: пустой пароль");
                return;
            }

            StatusMessage = "Регистрация...";
            Console.WriteLine($"[RegisterAsync] Отправка запроса: {Username}");

            var result = await _apiService.RegisterAsync(Username, Password);

            Console.WriteLine($"[RegisterAsync] Результат: Success={result.IsSuccess}, Error={result.ErrorMessage}");

            if (result.IsSuccess && result.Data != null)
            {
                // ВАЖНО: Показываем recovery code пользователю
                var recoveryCode = result.Data.RecoveryCode;

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // НЕ входим сразу - показываем код на экране входа
                    IsLoggedIn = false;
                    IsRegistering = false;
                    CurrentView = "Login";
                    Username = "";
                    Password = "";

                    // Показываем recovery code в отдельном копируемом поле
                    RecoveryCodeDisplay = recoveryCode;
                    ShowRecoveryCode = true;

                    LoginErrorMessage = $"✅ РЕГИСТРАЦИЯ УСПЕШНА!\n\n⚠️ СОХРАНИТЕ КОД ВОССТАНОВЛЕНИЯ НИЖЕ!\nВыделите и скопируйте его (Ctrl+C).\nОн понадобится для восстановления пароля.\nКод больше не будет показан!";
                    StatusMessage = $"Регистрация завершена. Сохраните код и войдите.";
                });

                Console.WriteLine($"[RegisterAsync] Регистрация успешна! Recovery code: {recoveryCode}");
                Console.WriteLine($"[RegisterAsync] Пользователь должен сохранить код и войти заново");
            }
            else
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = result.ErrorMessage ?? "Ошибка регистрации";
                    StatusMessage = "Ошибка регистрации";
                });
                Console.WriteLine($"[RegisterAsync] Ошибка: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoginErrorMessage = $"Ошибка подключения к серверу";
                StatusMessage = "Ошибка подключения";
            });
            Console.WriteLine($"[RegisterAsync] EXCEPTION: {ex.Message}");
            Console.WriteLine($"[RegisterAsync] Stack: {ex.StackTrace}");
        }
    }

    private async Task LoginAsync()
    {
        try
        {
            Console.WriteLine("[LoginAsync] Начало входа");

            // Очищаем предыдущие ошибки и скрываем recovery code
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
                    LoginErrorMessage = "Введите имя пользователя!";
                });
                Console.WriteLine("[LoginAsync] Ошибка: пустое имя");
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Введите пароль!";
                });
                Console.WriteLine("[LoginAsync] Ошибка: пустой пароль");
                return;
            }

            StatusMessage = "Вход...";
            Console.WriteLine($"[LoginAsync] Отправка запроса: {Username}");

            var result = await _apiService.LoginAsync(Username, Password);

            Console.WriteLine($"[LoginAsync] Результат: Success={result.IsSuccess}, Error={result.ErrorMessage}");

            if (result.IsSuccess && result.Data != null)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLoggedIn = true;
                    CurrentView = "Main";
                    StatusMessage = $"Добро пожаловать, {result.Data.Username}!";
                    Username = result.Data.Username;
                    UserEmail = result.Data.Email;
                    LoginErrorMessage = null;
                });

                // Сохраняем токен для автоматического входа
                SaveToken(result.Data.Token ?? "", result.Data.Username, result.Data.Email);

                Console.WriteLine("[LoginAsync] Вход успешен!");

                // Проверяем установку
                CheckInstallation();
                await CheckModpackVersionAsync();
                await LoadProfileAsync();
                await LoadServerStatusAsync();
                await LoadCurrentSkinAsync();
            }
            else
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = result.ErrorMessage ?? "Неверное имя пользователя или пароль";
                    StatusMessage = "Ошибка входа";
                });
                Console.WriteLine($"[LoginAsync] Ошибка: {result.ErrorMessage}");
                Console.WriteLine($"[LoginAsync] LoginErrorMessage установлен: {LoginErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoginErrorMessage = $"Ошибка подключения к серверу";
                StatusMessage = "Ошибка подключения";
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

            var authResult = _authService.AuthenticateOffline(Username);

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

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SkinStatus = "Скин загружен из кэша";
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

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SkinStatus = $"Скин загружен ({result.Data.SkinType})";
                });

                Console.WriteLine($"[LoadCurrentSkinAsync] Скин загружен с сервера: {result.Data.SkinType}");
            }
            else if (!File.Exists(localSkinPath))
            {
                // Нет ни локального, ни серверного скина
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
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
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        SkinStatus = "Скин загружен из кэша (офлайн)";
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
                CurrentSkinPreview = null;

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
}
