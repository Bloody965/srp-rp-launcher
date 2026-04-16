using System;
using System.IO;
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
    private readonly AudioService _audioService;
    private ModpackUpdater _modpackUpdater;
    private readonly ApiService _apiService;
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

        // Команды
        LoginCommand = ReactiveCommand.CreateFromTask(LoginAsync);
        RegisterCommand = ReactiveCommand.CreateFromTask(RegisterAsync);
        InstallCommand = ReactiveCommand.CreateFromTask(InstallMinecraftAsync);
        LaunchCommand = ReactiveCommand.CreateFromTask(LaunchGameAsync,
            this.WhenAnyValue(x => x.IsInstalled, x => x.IsGameRunning,
                (installed, running) => installed && !running));
        ChooseFolderCommand = ReactiveCommand.CreateFromTask(ChooseFolderAsync);
        UpdateModpackCommand = ReactiveCommand.CreateFromTask(UpdateModpackAsync);
        ToggleRegisterCommand = ReactiveCommand.Create(ToggleRegister);
        LogoutCommand = ReactiveCommand.Create(Logout);
        ResetPasswordCommand = ReactiveCommand.Create(ShowResetPassword);
        SendResetCodeCommand = ReactiveCommand.CreateFromTask(SendResetCodeAsync);
        ConfirmResetPasswordCommand = ReactiveCommand.CreateFromTask(ConfirmResetPasswordAsync);
        CancelResetCommand = ReactiveCommand.Create(CancelReset);
        EditNicknameCommand = ReactiveCommand.Create(StartEditNickname);
        SaveNicknameCommand = ReactiveCommand.CreateFromTask(SaveNicknameAsync);
        CancelEditNicknameCommand = ReactiveCommand.Create(CancelEditNickname);

        // Автоматический вход при запуске
        _ = TryAutoLoginAsync();
    }

    private string GetTokenFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var launcherDir = Path.Combine(appData, "SRP-RP-Launcher");
        Directory.CreateDirectory(launcherDir);
        return Path.Combine(launcherDir, "session.dat");
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
        set => this.RaiseAndSetIfChanged(ref _resetCodeSent, value);
    }

    private string _resetCode = "";
    public string ResetCode
    {
        get => _resetCode;
        set => this.RaiseAndSetIfChanged(ref _resetCode, value);
    }

    private string _newPassword = "";
    public string NewPassword
    {
        get => _newPassword;
        set => this.RaiseAndSetIfChanged(ref _newPassword, value);
    }

    private bool _isFullscreen;
    public bool IsFullscreen
    {
        get => _isFullscreen;
        set => this.RaiseAndSetIfChanged(ref _isFullscreen, value);
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

    public ReactiveCommand<Unit, Unit> LoginCommand { get; }
    public ReactiveCommand<Unit, Unit> RegisterCommand { get; }
    public ReactiveCommand<Unit, Unit> InstallCommand { get; }
    public ReactiveCommand<Unit, Unit> LaunchCommand { get; }
    public ReactiveCommand<Unit, Unit> ChooseFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> UpdateModpackCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleRegisterCommand { get; }
    public ReactiveCommand<Unit, Unit> LogoutCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetPasswordCommand { get; }
    public ReactiveCommand<Unit, Unit> SendResetCodeCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfirmResetPasswordCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelResetCommand { get; }
    public ReactiveCommand<Unit, Unit> EditNicknameCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveNicknameCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelEditNicknameCommand { get; }

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

            if (string.IsNullOrWhiteSpace(ResetCode))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Введите код из письма!";
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
            var result = await _apiService.ResetPasswordAsync(Email, ResetCode, NewPassword);

            if (result.IsSuccess)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsResettingPassword = false;
                    ResetCodeSent = false;
                    Email = "";
                    ResetCode = "";
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

            if (string.IsNullOrWhiteSpace(Email))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoginErrorMessage = "Введите email!";
                });
                Console.WriteLine("[RegisterAsync] Ошибка: пустой email");
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
            Console.WriteLine($"[RegisterAsync] Отправка запроса: {Username}, {Email}");

            var result = await _apiService.RegisterAsync(Username, Email, Password);

            Console.WriteLine($"[RegisterAsync] Результат: Success={result.IsSuccess}, Error={result.ErrorMessage}");

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

                Console.WriteLine("[RegisterAsync] Регистрация успешна!");

                // Проверяем установку
                CheckInstallation();
                await CheckModpackVersionAsync();
                await LoadProfileAsync();
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

                try
                {
                    var forgeSuccess = await _installer.InstallForgeAsync();
                    if (forgeSuccess)
                    {
                        Console.WriteLine("[InstallMinecraftAsync] Forge installed successfully!");
                        StatusMessage = "Forge установлен! Готов к запуску.";
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

            var authResult = _authService.AuthenticateOffline(Username);

            // CreateLaunchOptions автоматически определит Forge или vanilla
            var launchOptions = _installer.CreateLaunchOptions(authResult);

            // Устанавливаем полноэкранный режим
            launchOptions.IsFullscreen = IsFullscreen;

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
}
