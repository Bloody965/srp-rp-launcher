using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ApocalypseLauncher.Core;
using ApocalypseLauncher.Core.Models;

namespace ApocalypseLauncher.Core.Services;

public class GameLauncher
{
    private Process? _gameProcess;
    private StreamWriter? _logWriter;
    public event EventHandler<string>? OutputReceived;
    public event EventHandler? GameStarted;
    public event EventHandler<int>? GameExited;

    public GameLauncher()
    {
        // Создаем лог-файл
        var logDir = Path.Combine(Environment.CurrentDirectory, "logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, $"game_launch_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        _logWriter = new StreamWriter(logFile, true) { AutoFlush = true };
        Log("=== GameLauncher initialized ===");
    }

    private void Log(string message)
    {
        var logMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Console.WriteLine(logMessage);
        _logWriter?.WriteLine(logMessage);
    }

    public void LaunchGame(LaunchOptions options)
    {
        try
        {
            Log("=== LAUNCHING GAME ===");
            Log($"Java Path: {options.JavaPath}");
            Log($"Game Directory: {options.GameDirectory}");
            Log($"Version: {options.Version}");
            Log($"Username: {options.Username}");
            Log($"Main Class: {options.MainClass}");

            // Используем ArgumentList вместо Arguments для правильной обработки пробелов
            var startInfo = new ProcessStartInfo
            {
                FileName = options.JavaPath,
                WorkingDirectory = options.GameDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true, // Скрываем консоль игры
            };

            // Добавляем аргументы по одному - так они правильно экранируются
            BuildLaunchArgumentsList(options, startInfo.ArgumentList);

            Log($"Total arguments: {startInfo.ArgumentList.Count}");
            foreach (var arg in startInfo.ArgumentList)
            {
                Log($"  ARG: {arg}");
            }

            _gameProcess = new Process { StartInfo = startInfo };

            _gameProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Log($"[GAME OUTPUT] {e.Data}");
                    OutputReceived?.Invoke(this, e.Data);
                }
            };

            _gameProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Log($"[GAME ERROR] {e.Data}");
                    OutputReceived?.Invoke(this, $"[ERROR] {e.Data}");
                }
            };

            _gameProcess.Exited += (sender, e) =>
            {
                Log($"[GAME] Process exited with code: {_gameProcess.ExitCode}");
                GameExited?.Invoke(this, _gameProcess.ExitCode);
            };

            _gameProcess.EnableRaisingEvents = true;

            Log("Starting game process...");
            _gameProcess.Start();
            _gameProcess.BeginOutputReadLine();
            _gameProcess.BeginErrorReadLine();

            Log($"Game process started! PID: {_gameProcess.Id}");
            GameStarted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log($"[GAME LAUNCH ERROR] {ex.Message}");
            Log($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    private void BuildLaunchArgumentsList(LaunchOptions options, System.Collections.Generic.IList<string> args)
    {
        Log("Building launch arguments list...");

        // JVM аргументы
        args.Add($"-Xmx{options.MaxMemory}M");
        args.Add($"-Xms{options.MinMemory}M");
        args.Add("-XX:+UnlockExperimentalVMOptions");
        args.Add("-XX:+UseG1GC");
        args.Add("-XX:G1NewSizePercent=20");
        args.Add("-XX:G1ReservePercent=20");
        args.Add("-XX:MaxGCPauseMillis=50");
        args.Add("-XX:G1HeapRegionSize=32M");

        // Authlib-injector для кастомных скинов
        var authlibPath = Path.Combine(options.GameDirectory, "authlib-injector.jar");
        if (File.Exists(authlibPath))
        {
            Log("Adding authlib-injector for custom skins...");
            args.Add($"-javaagent:{authlibPath}={SrpProjectEndpoints.YggdrasilRootUrl}");
        }

        // Forge-специфичные JVM аргументы
        var isForge = options.MainClass == "cpw.mods.bootstraplauncher.BootstrapLauncher";
        if (isForge)
        {
            Log("Adding Forge-specific JVM arguments...");

            // Java module system opens - критически важно для Forge!
            args.Add("--add-opens");
            args.Add("java.base/java.util.jar=cpw.mods.securejarhandler");
            args.Add("--add-opens");
            args.Add("java.base/java.lang.invoke=cpw.mods.securejarhandler");
            args.Add("--add-exports");
            args.Add("java.base/sun.security.util=cpw.mods.securejarhandler");
            args.Add("--add-exports");
            args.Add("jdk.naming.dns/com.sun.jndi.dns=java.naming");

            // Forge system properties
            args.Add("-Djava.net.preferIPv6Addresses=system");
            args.Add("-Dforge.logging.console.level=info");
            args.Add("-Dforge.logging.markers=REGISTRIES");
            args.Add($"-DignoreList=bootstraplauncher,securejarhandler,asm-commons,asm-util,asm-analysis,asm-tree,asm,JarJarFileSystems,client-extra,fmlcore,javafmllanguage,lowcodelanguage,mclanguage,forge-,{options.Version}.jar");
            args.Add("-DmergeModules=jna-5.10.0.jar,jna-platform-5.10.0.jar");
            args.Add("-DlibraryDirectory=" + options.LibrariesDirectory);

            // КРИТИЧЕСКИ ВАЖНО: Module path для модульных JAR (ASM, bootstraplauncher и т.д.)
            var modulePath = string.Join(Path.PathSeparator.ToString(),
                Path.Combine(options.LibrariesDirectory, "cpw/mods/bootstraplauncher/1.1.2/bootstraplauncher-1.1.2.jar"),
                Path.Combine(options.LibrariesDirectory, "cpw/mods/securejarhandler/2.1.10/securejarhandler-2.1.10.jar"),
                Path.Combine(options.LibrariesDirectory, "org/ow2/asm/asm-commons/9.7/asm-commons-9.7.jar"),
                Path.Combine(options.LibrariesDirectory, "org/ow2/asm/asm-util/9.7/asm-util-9.7.jar"),
                Path.Combine(options.LibrariesDirectory, "org/ow2/asm/asm-analysis/9.7/asm-analysis-9.7.jar"),
                Path.Combine(options.LibrariesDirectory, "org/ow2/asm/asm-tree/9.7/asm-tree-9.7.jar"),
                Path.Combine(options.LibrariesDirectory, "org/ow2/asm/asm/9.7/asm-9.7.jar"),
                Path.Combine(options.LibrariesDirectory, "net/minecraftforge/JarJarFileSystems/0.3.19/JarJarFileSystems-0.3.19.jar")
            );
            args.Add("-p");
            args.Add(modulePath);

            // Добавляем все модули из module path
            args.Add("--add-modules");
            args.Add("ALL-MODULE-PATH");
        }

        // Нативные библиотеки
        var nativesPath = options.NativesDirectory;
        if (!Directory.Exists(nativesPath))
        {
            Log($"Creating natives directory: {nativesPath}");
            Directory.CreateDirectory(nativesPath);
        }
        args.Add($"-Djava.library.path={nativesPath}");

        // Classpath
        var classpath = BuildClasspath(options);
        Log($"Classpath entries: {classpath.Split(Path.PathSeparator).Length}");
        args.Add("-cp");
        args.Add(classpath);

        // Главный класс
        args.Add(options.MainClass);

        // Аргументы игры - каждый отдельно!
        // Для Forge нужны специальные аргументы
        if (isForge)
        {
            args.Add("--launchTarget");
            args.Add("forgeclient");
            args.Add("--fml.forgeVersion");
            args.Add("47.3.0");
            args.Add("--fml.mcVersion");
            args.Add("1.20.1");
            args.Add("--fml.forgeGroup");
            args.Add("net.minecraftforge");
            args.Add("--fml.mcpVersion");
            args.Add("20230612.114412");
        }

        args.Add("--username");
        args.Add(options.Username);
        args.Add("--uuid");
        args.Add(options.UUID);
        args.Add("--accessToken");
        args.Add(options.AccessToken);
        args.Add("--version");
        args.Add(options.Version);
        args.Add("--gameDir");
        args.Add(options.GameDirectory); // Без кавычек! ArgumentList сам обработает пробелы
        args.Add("--assetsDir");
        args.Add(options.AssetsDirectory);
        args.Add("--assetIndex");
        args.Add(options.AssetIndex);
        args.Add("--width");
        args.Add(options.WindowWidth.ToString());
        args.Add("--height");
        args.Add(options.WindowHeight.ToString());

        if (options.IsFullscreen)
            args.Add("--fullscreen");

        Log($"Total arguments added: {args.Count}");
    }

    private string BuildLaunchArguments(LaunchOptions options)
    {
        var args = new List<string>();

        Console.WriteLine("Building launch arguments...");

        // JVM аргументы
        args.Add($"-Xmx{options.MaxMemory}M");
        args.Add($"-Xms{options.MinMemory}M");
        args.Add("-XX:+UnlockExperimentalVMOptions");
        args.Add("-XX:+UseG1GC");
        args.Add("-XX:G1NewSizePercent=20");
        args.Add("-XX:G1ReservePercent=20");
        args.Add("-XX:MaxGCPauseMillis=50");
        args.Add("-XX:G1HeapRegionSize=32M");

        // Нативные библиотеки
        var nativesPath = options.NativesDirectory;
        if (!Directory.Exists(nativesPath))
        {
            Console.WriteLine($"Creating natives directory: {nativesPath}");
            Directory.CreateDirectory(nativesPath);
        }
        args.Add($"-Djava.library.path={nativesPath}");

        // Classpath
        var classpath = BuildClasspath(options);
        Console.WriteLine($"Classpath entries: {classpath.Split(Path.PathSeparator).Length}");
        args.Add("-cp");
        args.Add(classpath);

        // Главный класс
        args.Add(options.MainClass);

        // Аргументы игры (БЕЗ кавычек - они добавятся автоматически при необходимости)
        args.Add("--username");
        args.Add(options.Username);
        args.Add("--uuid");
        args.Add(options.UUID);
        args.Add("--accessToken");
        args.Add(options.AccessToken);
        args.Add("--version");
        args.Add(options.Version);
        args.Add("--gameDir");
        args.Add(options.GameDirectory);
        args.Add("--assetsDir");
        args.Add(options.AssetsDirectory);
        args.Add("--assetIndex");
        args.Add(options.AssetIndex);
        args.Add("--width");
        args.Add(options.WindowWidth.ToString());
        args.Add("--height");
        args.Add(options.WindowHeight.ToString());

        if (options.IsFullscreen)
            args.Add("--fullscreen");

        var result = string.Join(" ", args);
        Console.WriteLine($"Final arguments length: {result.Length} chars");
        return result;
    }

    private string BuildClasspath(LaunchOptions options)
    {
        var separator = Path.PathSeparator;
        var classpathEntries = new List<string>();

        Log("Building classpath...");

        // Список JAR которые НЕ должны быть в classpath (они в module path или не нужны)
        var excludedJars = new HashSet<string>
        {
            "bootstraplauncher-1.1.2.jar",
            "securejarhandler-2.1.10.jar",
            "asm-commons-9.7.jar",
            "asm-util-9.7.jar",
            "asm-analysis-9.7.jar",
            "asm-tree-9.7.jar",
            "asm-9.7.jar",
            "JarJarFileSystems-0.3.19.jar",
            "ForgeAutoRenamingTool-0.1.22-all.jar",
            "binarypatcher-1.1.1.jar",
            "installertools-1.4.1.jar",
            "jarsplitter-1.1.4.jar",
            "client-1.20.1-20230612.114412-slim.jar",
            "client-1.20.1-20230612.114412-extra.jar",
            "client-1.20.1-20230612.114412-srg.jar",
            "1.20.1.jar" // Vanilla JAR - Forge загружает его сам
        };

        // Добавляем все JAR файлы из libraries кроме исключенных
        var librariesPath = options.LibrariesDirectory;
        if (Directory.Exists(librariesPath))
        {
            var jarFiles = Directory.GetFiles(librariesPath, "*.jar", SearchOption.AllDirectories)
                .Where(jar => !excludedJars.Contains(Path.GetFileName(jar)))
                .ToList();
            Log($"Found {jarFiles.Count} library JARs (excluded {excludedJars.Count} modular/installer JARs)");
            classpathEntries.AddRange(jarFiles);
        }
        else
        {
            Log($"WARNING: Libraries directory not found: {librariesPath}");
        }

        // Для vanilla обязательно добавляем version JAR, иначе main class не будет найден.
        var isForge = options.MainClass == "cpw.mods.bootstraplauncher.BootstrapLauncher";
        if (!isForge)
        {
            var vanillaJarPath = Path.Combine(options.GameDirectory, "versions", options.Version, $"{options.Version}.jar");
            if (File.Exists(vanillaJarPath))
            {
                classpathEntries.Add(vanillaJarPath);
                Log($"Added vanilla version JAR to classpath: {vanillaJarPath}");
            }
            else
            {
                Log($"WARNING: Vanilla version JAR not found: {vanillaJarPath}");
            }
        }

        if (classpathEntries.Count == 0)
        {
            Log("ERROR: No classpath entries found!");
        }

        return string.Join(separator, classpathEntries);
    }

    public void StopGame()
    {
        if (_gameProcess != null && !_gameProcess.HasExited)
        {
            _gameProcess.Kill();
            _gameProcess.Dispose();
            _gameProcess = null;
        }
    }

    public bool IsGameRunning()
    {
        return _gameProcess != null && !_gameProcess.HasExited;
    }
}
