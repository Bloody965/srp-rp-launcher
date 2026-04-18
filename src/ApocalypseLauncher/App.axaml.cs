using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ApocalypseLauncher.ViewModels;
using ApocalypseLauncher.Views;
using System;
using System.Text;

namespace ApocalypseLauncher;

public partial class App : Application
{
    public override void Initialize()
    {
        // Устанавливаем UTF-8 кодировку для консоли (только если консоль доступна)
        try
        {
            if (Console.OutputEncoding.CodePage != 65001)
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
        }
        catch
        {
            // Игнорируем ошибку если консоль недоступна (GUI режим)
        }

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
