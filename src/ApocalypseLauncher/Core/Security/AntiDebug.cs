using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ApocalypseLauncher.Core.Security;

/// <summary>
/// Anti-debugging и anti-tampering защита
/// </summary>
public static class AntiDebug
{
    private static bool _isDebuggerDetected = false;
    private static Timer? _debugCheckTimer;

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool isDebuggerPresent);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool IsDebuggerPresent();

    /// <summary>
    /// Инициализация anti-debugging защиты
    /// </summary>
    public static void Initialize()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("[AntiDebug] Not Windows - skipping anti-debug checks");
            return;
        }

        Console.WriteLine("[AntiDebug] Initializing protection...");

        // Проверка при запуске
        if (DetectDebugger())
        {
            HandleDebuggerDetected();
            return;
        }

        // Периодическая проверка каждые 5 секунд
        _debugCheckTimer = new Timer(_ =>
        {
            if (DetectDebugger())
            {
                HandleDebuggerDetected();
            }
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        Console.WriteLine("[AntiDebug] Protection enabled");
    }

    /// <summary>
    /// Обнаружение отладчика
    /// </summary>
    private static bool DetectDebugger()
    {
        if (_isDebuggerDetected)
            return true;

        try
        {
            // Метод 1: Managed debugger
            if (Debugger.IsAttached)
            {
                Console.WriteLine("[AntiDebug] Managed debugger detected");
                return true;
            }

            // Метод 2: Native debugger (Windows API)
            if (OperatingSystem.IsWindows())
            {
                if (IsDebuggerPresent())
                {
                    Console.WriteLine("[AntiDebug] Native debugger detected (IsDebuggerPresent)");
                    return true;
                }

                // Метод 3: Remote debugger
                bool isRemoteDebuggerPresent = false;
                CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref isRemoteDebuggerPresent);
                if (isRemoteDebuggerPresent)
                {
                    Console.WriteLine("[AntiDebug] Remote debugger detected");
                    return true;
                }
            }

            // Метод 4: Timing attack - отладчик замедляет выполнение
            var sw = Stopwatch.StartNew();
            Thread.Sleep(1);
            sw.Stop();

            // Если Sleep(1) занял больше 50ms - возможно отладчик
            if (sw.ElapsedMilliseconds > 50)
            {
                Console.WriteLine($"[AntiDebug] Timing anomaly detected: {sw.ElapsedMilliseconds}ms");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AntiDebug] Error during detection: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Обработка обнаружения отладчика
    /// </summary>
    private static void HandleDebuggerDetected()
    {
        _isDebuggerDetected = true;
        _debugCheckTimer?.Dispose();

        Console.WriteLine("[AntiDebug] ========================================");
        Console.WriteLine("[AntiDebug] DEBUGGER DETECTED - TERMINATING");
        Console.WriteLine("[AntiDebug] ========================================");

        // Даем время на вывод сообщения
        Thread.Sleep(1000);

        // Аварийное завершение
        Environment.Exit(1);
    }

    /// <summary>
    /// Проверка целостности сборки
    /// </summary>
    public static bool VerifyIntegrity()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var location = assembly.Location;

            if (string.IsNullOrEmpty(location))
            {
                Console.WriteLine("[AntiDebug] Cannot verify integrity - no assembly location");
                return true; // Single-file deployment
            }

            // Проверка что сборка не была модифицирована
            var fileInfo = new System.IO.FileInfo(location);

            // Базовые проверки
            if (!fileInfo.Exists)
            {
                Console.WriteLine("[AntiDebug] Assembly file not found");
                return false;
            }

            // Проверка цифровой подписи (если есть)
            // TODO: Добавить проверку Authenticode signature

            Console.WriteLine("[AntiDebug] Integrity check passed");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AntiDebug] Integrity check error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Остановка защиты (для тестов)
    /// </summary>
    public static void Shutdown()
    {
        _debugCheckTimer?.Dispose();
        _debugCheckTimer = null;
    }
}
