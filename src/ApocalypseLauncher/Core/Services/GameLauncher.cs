using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using ApocalypseLauncher.Core;
using ApocalypseLauncher.Core.Models;

namespace ApocalypseLauncher.Core.Services;

public class GameLauncher
{
    private Process? _gameProcess;
    private StreamWriter? _logWriter;
    private readonly string _logDirectory;
    public event EventHandler<string>? OutputReceived;
    public event EventHandler? GameStarted;
    public event EventHandler<int>? GameExited;

    public GameLauncher()
    {
        _logDirectory = ResolveLogDirectory();
        Directory.CreateDirectory(_logDirectory);
        var logFile = Path.Combine(_logDirectory, $"game_launch_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        _logWriter = new StreamWriter(logFile, true) { AutoFlush = true };
        Log($"=== GameLauncher initialized (logs: {_logDirectory}) ===");
    }

    private static string ResolveLogDirectory()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                return Path.Combine(appData, "SRP-RP-Launcher", "logs");
            }
        }
        catch
        {
            // fallback below
        }

        return Path.Combine(AppContext.BaseDirectory, "logs");
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

            var forgeVersionJsonPath = Path.Combine(options.GameDirectory, "versions", options.Version, $"{options.Version}.json");
            if (!TryAppendForgeJvmArgumentsFromVersionJson(forgeVersionJsonPath, options, args))
            {
                Log("WARNING: Forge version.json JVM args not found/parsed; using legacy hardcoded Forge JVM args");

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
            if (!TryAppendForgeGameArgumentsFromVersionJson(options, args))
            {
                var forgeVersionArg = ExtractForgeVersion(options.Version) ?? "47.4.0";
                var mcVersionArg = ExtractMinecraftVersion(options.Version) ?? "1.20.1";

                args.Add("--launchTarget");
                args.Add("forgeclient");
                args.Add("--fml.forgeVersion");
                args.Add(forgeVersionArg);
                args.Add("--fml.mcVersion");
                args.Add(mcVersionArg);
                args.Add("--fml.forgeGroup");
                args.Add("net.minecraftforge");
                args.Add("--fml.mcpVersion");
                args.Add("20230612.114412");
            }
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

        var isForge = options.MainClass == "cpw.mods.bootstraplauncher.BootstrapLauncher";
        var librariesPath = options.LibrariesDirectory;
        if (Directory.Exists(librariesPath))
        {
            if (isForge)
            {
                // Для Forge берем библиотеки строго из version.json текущей версии.
                // Это исключает старые/дублирующиеся JAR из libraries, которые ломают module resolution.
                var forgeJsonPath = Path.Combine(options.GameDirectory, "versions", options.Version, $"{options.Version}.json");
                if (File.Exists(forgeJsonPath))
                {
                    try
                    {
                        var fromJson = CollectForgeClasspathJarsFromVersionChain(forgeJsonPath, librariesPath);
                        classpathEntries.AddRange(fromJson.Distinct(StringComparer.OrdinalIgnoreCase));
                        Log($"Forge classpath from version chain: {classpathEntries.Count} entries");
                    }
                    catch (Exception ex)
                    {
                        Log($"WARNING: Failed to parse Forge version json ({forgeJsonPath}): {ex.Message}");
                    }
                }
            }

            // Fallback или vanilla: старый механизм сканирования libraries.
            if (classpathEntries.Count == 0)
            {
                var jarFiles = Directory.GetFiles(librariesPath, "*.jar", SearchOption.AllDirectories)
                    .Where(jar => !ShouldExcludeClasspathJarByRelativePath(librariesPath, jar))
                    .ToList();
                Log($"Fallback libraries scan: {jarFiles.Count} JARs");
                classpathEntries.AddRange(jarFiles);
            }
        }
        else
        {
            Log($"WARNING: Libraries directory not found: {librariesPath}");
        }

        // Для vanilla обязательно добавляем version JAR, иначе main class не будет найден.
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

    private static bool TryAppendForgeJvmArgumentsFromVersionJson(string forgeVersionJsonPath, LaunchOptions options, IList<string> args)
    {
        if (!File.Exists(forgeVersionJsonPath))
        {
            return false;
        }

        try
        {
            var root = JObject.Parse(File.ReadAllText(forgeVersionJsonPath));
            var jvmArgs = root["arguments"]?["jvm"] as JArray;
            if (jvmArgs == null)
            {
                return false;
            }

            var expandedTokens = new List<string>();
            foreach (var token in jvmArgs)
            {
                if (token.Type != JTokenType.String)
                {
                    continue;
                }

                var expanded = ExpandForgeTemplates(token.Value<string>() ?? string.Empty, options);
                if (string.IsNullOrWhiteSpace(expanded))
                {
                    continue;
                }

                expandedTokens.Add(expanded);
            }

            var pending = new List<string>();
            for (var i = 0; i < expandedTokens.Count; i++)
            {
                var current = expandedTokens[i];

                if (string.Equals(current, "-p", StringComparison.Ordinal))
                {
                    if (i + 1 >= expandedTokens.Count)
                    {
                        return false;
                    }

                    var modulePath = expandedTokens[i + 1];
                    if (string.IsNullOrWhiteSpace(modulePath) || modulePath.StartsWith('-'))
                    {
                        return false;
                    }

                    pending.Add("-p");
                    pending.Add(modulePath);
                    i++;
                    continue;
                }

                pending.Add(current);
            }

            foreach (var arg in pending)
            {
                args.Add(arg);
            }

            return pending.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryAppendForgeGameArgumentsFromVersionJson(LaunchOptions options, IList<string> args)
    {
        var forgeVersionJsonPath = Path.Combine(options.GameDirectory, "versions", options.Version, $"{options.Version}.json");
        if (!File.Exists(forgeVersionJsonPath))
        {
            return false;
        }

        try
        {
            var root = JObject.Parse(File.ReadAllText(forgeVersionJsonPath));
            var gameArgs = root["arguments"]?["game"] as JArray;
            if (gameArgs == null)
            {
                return false;
            }

            foreach (var token in gameArgs)
            {
                if (token.Type != JTokenType.String)
                {
                    continue;
                }

                var expanded = ExpandForgeTemplates(token.Value<string>() ?? string.Empty, options);
                if (!string.IsNullOrWhiteSpace(expanded))
                {
                    args.Add(expanded);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ExpandForgeTemplates(string value, LaunchOptions options)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var expanded = value
            .Replace("${library_directory}", options.LibrariesDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Replace("${version_name}", options.Version)
            .Replace("${classpath_separator}", Path.PathSeparator.ToString());

        return expanded;
    }

    private static List<string> CollectForgeClasspathJarsFromVersionChain(string startingVersionJsonPath, string librariesRoot)
    {
        var result = new List<string>();
        var visitedJson = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Walk(string jsonPath)
        {
            if (!File.Exists(jsonPath) || !visitedJson.Add(jsonPath))
            {
                return;
            }

            var json = JObject.Parse(File.ReadAllText(jsonPath));

            if (json["libraries"] is JArray libraries)
            {
                foreach (var library in libraries)
                {
                    var artifactPath = library["downloads"]?["artifact"]?["path"]?.ToString();
                    if (string.IsNullOrWhiteSpace(artifactPath))
                    {
                        continue;
                    }

                    var absoluteJar = Path.Combine(librariesRoot, artifactPath.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(absoluteJar))
                    {
                        continue;
                    }

                    if (!ShouldExcludeClasspathJarByRelativePath(librariesRoot, absoluteJar))
                    {
                        result.Add(absoluteJar);
                    }
                }
            }

            var inheritsFrom = json["inheritsFrom"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(inheritsFrom))
            {
                return;
            }

            var parentJsonPath = Path.Combine(Path.GetDirectoryName(jsonPath)!, "..", inheritsFrom, $"{inheritsFrom}.json");
            parentJsonPath = Path.GetFullPath(parentJsonPath);
            Walk(parentJsonPath);
        }

        Walk(startingVersionJsonPath);
        return result;
    }

    private static bool ShouldExcludeClasspathJarByRelativePath(string librariesRoot, string absoluteJarPath)
    {
        try
        {
            var rel = Path.GetRelativePath(librariesRoot, absoluteJarPath)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            if (rel.Contains("..", StringComparison.Ordinal))
            {
                return true;
            }

            // These belong to the Forge bootstrap module layer, not the game classpath.
            if (rel.StartsWith("cpw" + Path.DirectorySeparatorChar + "mods" + Path.DirectorySeparatorChar + "bootstraplauncher" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (rel.StartsWith("cpw" + Path.DirectorySeparatorChar + "mods" + Path.DirectorySeparatorChar + "securejarhandler" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (rel.StartsWith("org" + Path.DirectorySeparatorChar + "ow2" + Path.DirectorySeparatorChar + "asm" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (rel.StartsWith("net" + Path.DirectorySeparatorChar + "minecraftforge" + Path.DirectorySeparatorChar + "JarJarFileSystems" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var fileName = Path.GetFileName(rel);
            if (fileName.StartsWith("client-", StringComparison.OrdinalIgnoreCase) &&
                fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(fileName, "1.20.1.jar", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (fileName.Contains("ForgeAutoRenamingTool", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("binarypatcher", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("installertools", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("jarsplitter", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
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

    private static string? ExtractForgeVersion(string versionId)
    {
        const string marker = "-forge-";
        var idx = versionId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var start = idx + marker.Length;
        return start < versionId.Length ? versionId[start..] : null;
    }

    private static string? ExtractMinecraftVersion(string versionId)
    {
        const string marker = "-forge-";
        var idx = versionId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx <= 0)
        {
            return null;
        }

        return versionId[..idx];
    }
}
