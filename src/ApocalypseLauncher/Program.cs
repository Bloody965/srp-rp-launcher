using System;
using Avalonia;
using Avalonia.ReactiveUI;

namespace ApocalypseLauncher;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            Console.WriteLine("=== APOCALYPSE LAUNCHER STARTING ===");
            Console.WriteLine($"Time: {DateTime.Now}");
            Console.WriteLine($"Working Directory: {Environment.CurrentDirectory}");
            Console.WriteLine($"Args: {string.Join(" ", args)}");
            Console.WriteLine("====================================");
            Console.WriteLine();

            // Инициализация системы безопасности
            Console.WriteLine("[Security] Initializing protection systems...");

            // Anti-debugging защита
            Core.Security.AntiDebug.Initialize();

            // Проверка целостности
            if (!Core.Security.AntiDebug.VerifyIntegrity())
            {
                Console.WriteLine("[Security] INTEGRITY CHECK FAILED - Launcher may be modified!");
                Console.WriteLine("[Security] Please re-download the launcher from official source.");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                Environment.Exit(1);
                return;
            }

            Console.WriteLine("[Security] All protection systems active");
            Console.WriteLine();

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);

            Console.WriteLine();
            Console.WriteLine("=== LAUNCHER CLOSED NORMALLY ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("=== FATAL ERROR ===");
            Console.WriteLine($"Message: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                Console.WriteLine($"Inner Stack Trace: {ex.InnerException.StackTrace}");
            }
            Console.WriteLine("===================");
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}
