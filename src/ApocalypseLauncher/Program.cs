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
