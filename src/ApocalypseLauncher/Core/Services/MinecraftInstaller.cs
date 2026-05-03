using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ApocalypseLauncher.Core.Models;
using Newtonsoft.Json.Linq;
using ICSharpCode.SharpZipLib.Zip;

namespace ApocalypseLauncher.Core.Services;

public class MinecraftInstaller
{
    private readonly DownloadService _downloadService;
    private readonly string _minecraftDirectory;
    private const string VERSION = "1.20.1";
    private const string FORGE_VERSION = "47.4.0"; // Версия Forge, требуемая текущей сборкой

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<int>? ProgressChanged;

    public MinecraftInstaller(string minecraftDirectory)
    {
        _minecraftDirectory = minecraftDirectory;
        _downloadService = new DownloadService();
        _downloadService.ProgressChanged += (s, p) => ProgressChanged?.Invoke(this, p);
        _downloadService.StatusChanged += (s, msg) =>
        {
            Console.WriteLine($"[Download] {msg}");
            StatusChanged?.Invoke(this, msg);
        };
    }

    public async Task<bool> InstallMinecraftAsync()
    {
        try
        {
            Console.WriteLine("=== Starting Minecraft Installation ===");
            StatusChanged?.Invoke(this, "Получение информации о версии...");

            // Создаем структуру папок
            CreateDirectories();

            // Скачиваем манифест версий
            var versionManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
            Console.WriteLine($"Fetching version manifest from: {versionManifestUrl}");

            StatusChanged?.Invoke(this, "Загрузка списка версий...");
            var manifest = await _downloadService.DownloadJsonAsync(versionManifestUrl);

            // Находим версию 1.20.1
            var versionInfo = manifest["versions"]?
                .FirstOrDefault(v => v["id"]?.ToString() == VERSION);

            if (versionInfo == null)
            {
                var error = $"Version {VERSION} not found in manifest";
                Console.WriteLine($"ERROR: {error}");
                throw new Exception(error);
            }

            var versionUrl = versionInfo["url"]?.ToString();
            if (string.IsNullOrEmpty(versionUrl))
            {
                var error = "Version URL is empty";
                Console.WriteLine($"ERROR: {error}");
                throw new Exception(error);
            }

            Console.WriteLine($"Version URL: {versionUrl}");
            StatusChanged?.Invoke(this, "Загрузка информации о версии...");
            var versionJson = await _downloadService.DownloadJsonAsync(versionUrl);

            // Сохраняем JSON версии
            var versionDir = Path.Combine(_minecraftDirectory, "versions", VERSION);
            Directory.CreateDirectory(versionDir);
            var versionJsonPath = Path.Combine(versionDir, $"{VERSION}.json");
            File.WriteAllText(versionJsonPath, versionJson.ToString());
            Console.WriteLine($"Saved version JSON to: {versionJsonPath}");

            // Скачиваем клиент
            StatusChanged?.Invoke(this, "Загрузка клиента Minecraft...");
            var clientUrl = versionJson["downloads"]?["client"]?["url"]?.ToString();
            if (!string.IsNullOrEmpty(clientUrl))
            {
                Console.WriteLine($"Downloading client from: {clientUrl}");
                var clientPath = Path.Combine(versionDir, $"{VERSION}.jar");
                await _downloadService.DownloadFileAsync(clientUrl, clientPath);
                Console.WriteLine($"Client downloaded to: {clientPath}");
            }
            else
            {
                Console.WriteLine("WARNING: Client URL not found");
            }

            // Скачиваем библиотеки
            StatusChanged?.Invoke(this, "Загрузка библиотек...");
            Console.WriteLine("Starting libraries download...");
            await DownloadLibrariesAsync(versionJson);

            // Скачиваем ассеты
            StatusChanged?.Invoke(this, "Загрузка ресурсов...");
            Console.WriteLine("Starting assets download...");
            await DownloadAssetsAsync(versionJson);

            StatusChanged?.Invoke(this, "Minecraft установлен успешно!");
            Console.WriteLine("=== Minecraft Installation Complete ===");
            return true;
        }
        catch (Exception ex)
        {
            var errorMsg = $"Ошибка: {ex.Message}";
            Console.WriteLine($"FATAL ERROR: {errorMsg}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            StatusChanged?.Invoke(this, errorMsg);
            return false;
        }
    }

    public async Task<bool> InstallForgeAsync()
    {
        try
        {
            StatusChanged?.Invoke(this, "Загрузка Forge installer...");
            Console.WriteLine("=== Installing Forge via official installer ===");

            // Создаем launcher_profiles.json который требует Forge installer
            var launcherProfilesPath = Path.Combine(_minecraftDirectory, "launcher_profiles.json");
            if (!File.Exists(launcherProfilesPath))
            {
                Console.WriteLine("Creating launcher_profiles.json for Forge installer...");
                var launcherProfiles = new JObject
                {
                    ["clientToken"] = Guid.NewGuid().ToString("N"),
                    ["launcherVersion"] = new JObject
                    {
                        ["name"] = "2.1.1349",
                        ["format"] = 21
                    },
                    ["profiles"] = new JObject
                    {
                        ["(Default)"] = new JObject
                        {
                            ["name"] = "(Default)",
                            ["lastVersionId"] = VERSION
                        }
                    },
                    ["selectedProfile"] = "(Default)",
                    ["authenticationDatabase"] = new JObject()
                };
                File.WriteAllText(launcherProfilesPath, launcherProfiles.ToString(Newtonsoft.Json.Formatting.Indented));
                Console.WriteLine($"Created launcher_profiles.json: {launcherProfilesPath}");
            }

            // Проверяем что vanilla версия установлена
            var vanillaJsonPath = Path.Combine(_minecraftDirectory, "versions", VERSION, $"{VERSION}.json");
            var vanillaJarPath = Path.Combine(_minecraftDirectory, "versions", VERSION, $"{VERSION}.jar");

            if (!File.Exists(vanillaJsonPath) || !File.Exists(vanillaJarPath))
            {
                Console.WriteLine("Vanilla version not found, installing first...");
                StatusChanged?.Invoke(this, "Установка базовой версии Minecraft...");
                var vanillaSuccess = await InstallMinecraftAsync();
                if (!vanillaSuccess)
                {
                    Console.WriteLine("Failed to install vanilla version");
                    return false;
                }
            }

            // Скачиваем официальный Forge installer
            var installerUrl = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{VERSION}-{FORGE_VERSION}/forge-{VERSION}-{FORGE_VERSION}-installer.jar";
            var installerPath = Path.Combine(Path.GetTempPath(), "forge-installer.jar");

            Console.WriteLine($"Downloading Forge installer from: {installerUrl}");
            using (var client = new HttpClient())
            {
                var response = await client.GetAsync(installerUrl);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Failed to download Forge installer: {response.StatusCode}");
                    StatusChanged?.Invoke(this, "Ошибка загрузки Forge installer");
                    return false;
                }

                var installerBytes = await response.Content.ReadAsByteArrayAsync();
                File.WriteAllBytes(installerPath, installerBytes);
                Console.WriteLine($"Forge installer downloaded: {installerPath}");
            }

            // Запускаем Forge installer
            StatusChanged?.Invoke(this, "Установка Forge (это может занять минуту)...");
            Console.WriteLine("Running Forge installer...");

            var javaPath = FindJavaPath();
            var startInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                WorkingDirectory = Path.GetTempPath(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Добавляем аргументы через ArgumentList
            startInfo.ArgumentList.Add("-jar");
            startInfo.ArgumentList.Add(installerPath);
            startInfo.ArgumentList.Add("--installClient");
            startInfo.ArgumentList.Add(_minecraftDirectory);

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    Console.WriteLine("Failed to start Forge installer");
                    StatusChanged?.Invoke(this, "Ошибка запуска Forge installer");
                    return false;
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                Console.WriteLine($"Forge installer output:\n{output}");
                if (!string.IsNullOrEmpty(error))
                {
                    Console.WriteLine($"Forge installer errors:\n{error}");
                }

                if (process.ExitCode == 0)
                {
                    Console.WriteLine("Forge installed successfully!");
                    StatusChanged?.Invoke(this, "Forge установлен успешно!");

                    // Удаляем installer
                    try { File.Delete(installerPath); } catch { }

                    return true;
                }
                else
                {
                    Console.WriteLine($"Forge installer failed with exit code: {process.ExitCode}");
                    StatusChanged?.Invoke(this, $"Ошибка установки Forge (код: {process.ExitCode})");
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            var errorMsg = $"Ошибка установки Forge: {ex.Message}";
            Console.WriteLine($"FATAL ERROR: {errorMsg}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            StatusChanged?.Invoke(this, errorMsg);
            return false;
        }
    }

    private async Task DownloadLibrariesAsync(JObject versionJson)
    {
        var libraries = versionJson["libraries"] as JArray;
        if (libraries == null) return;

        var librariesDir = Path.Combine(_minecraftDirectory, "libraries");

        StatusChanged?.Invoke(this, $"Загрузка библиотек: 0/{libraries.Count}");
        Console.WriteLine($"Total libraries to download: {libraries.Count}");

        int downloaded = 0;
        int skipped = 0;

        foreach (var library in libraries)
        {
            // Проверяем правила (rules) для библиотеки
            var rules = library["rules"] as JArray;
            if (rules != null && !CheckLibraryRules(rules))
            {
                skipped++;
                continue;
            }

            var downloads = library["downloads"];
            var artifact = downloads?["artifact"];

            if (artifact != null)
            {
                var url = artifact["url"]?.ToString();
                var path = artifact["path"]?.ToString();

                if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(path))
                {
                    var destinationPath = Path.Combine(librariesDir, path);

                    if (!File.Exists(destinationPath))
                    {
                        Console.WriteLine($"Downloading library: {path}");
                        await _downloadService.DownloadFileAsync(url, destinationPath);
                        downloaded++;
                    }
                    else
                    {
                        Console.WriteLine($"Skipping existing library: {path}");
                        skipped++;
                    }
                }
            }

            // Загружаем нативные библиотеки
            var classifiers = downloads?["classifiers"];
            if (classifiers != null)
            {
                var nativesKey = GetNativesKey();
                var nativeLib = classifiers[nativesKey];

                if (nativeLib != null)
                {
                    var url = nativeLib["url"]?.ToString();
                    var path = nativeLib["path"]?.ToString();

                    if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(path))
                    {
                        var destinationPath = Path.Combine(librariesDir, path);

                        if (!File.Exists(destinationPath))
                        {
                            Console.WriteLine($"Downloading native library: {path}");
                            await _downloadService.DownloadFileAsync(url, destinationPath);
                            downloaded++;
                        }
                        else
                        {
                            Console.WriteLine($"Skipping existing native: {path}");
                            skipped++;
                        }
                    }
                }
            }

            if ((downloaded + skipped) % 10 == 0)
            {
                StatusChanged?.Invoke(this, $"Библиотеки: {downloaded} загружено, {skipped} пропущено из {libraries.Count}");
            }
        }

        StatusChanged?.Invoke(this, $"Библиотеки: {downloaded} загружено, {skipped} пропущено");
        Console.WriteLine($"Libraries complete: {downloaded} downloaded, {skipped} skipped");
    }

    private bool CheckLibraryRules(JArray rules)
    {
        foreach (var rule in rules)
        {
            var action = rule["action"]?.ToString();
            var os = rule["os"];

            if (os != null)
            {
                var osName = os["name"]?.ToString();
                var currentOs = GetCurrentOsName();

                if (action == "allow" && osName == currentOs)
                    return true;
                if (action == "disallow" && osName == currentOs)
                    return false;
            }
        }

        return true;
    }

    private string GetCurrentOsName()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsLinux()) return "linux";
        if (OperatingSystem.IsMacOS()) return "osx";
        return "unknown";
    }

    private string GetNativesKey()
    {
        if (OperatingSystem.IsWindows()) return "natives-windows";
        if (OperatingSystem.IsLinux()) return "natives-linux";
        if (OperatingSystem.IsMacOS()) return "natives-osx";
        return "natives-windows";
    }

    private async Task DownloadAssetsAsync(JObject versionJson)
    {
        var assetIndex = versionJson["assetIndex"];
        var assetIndexUrl = assetIndex?["url"]?.ToString();
        var assetIndexId = assetIndex?["id"]?.ToString();

        if (string.IsNullOrEmpty(assetIndexUrl) || string.IsNullOrEmpty(assetIndexId))
            return;

        var indexesDir = Path.Combine(_minecraftDirectory, "assets", "indexes");
        Directory.CreateDirectory(indexesDir);

        var indexPath = Path.Combine(indexesDir, $"{assetIndexId}.json");

        Console.WriteLine($"Downloading asset index: {assetIndexUrl}");
        await _downloadService.DownloadFileAsync(assetIndexUrl, indexPath);

        var indexJson = JObject.Parse(File.ReadAllText(indexPath));
        var objects = indexJson["objects"] as JObject;

        if (objects == null) return;

        var objectsDir = Path.Combine(_minecraftDirectory, "assets", "objects");
        var totalAssets = objects.Count;

        StatusChanged?.Invoke(this, $"Загрузка ассетов (звуки, текстуры, музыка): 0/{totalAssets}");
        Console.WriteLine($"Total assets to download: {totalAssets}");

        // Фильтруем только те, которые нужно скачать
        var assetsToDownload = new List<(string url, string path)>();

        foreach (var obj in objects.Properties())
        {
            var hash = obj.Value["hash"]?.ToString();
            if (string.IsNullOrEmpty(hash)) continue;

            var hashPrefix = hash.Substring(0, 2);
            var assetUrl = $"https://resources.download.minecraft.net/{hashPrefix}/{hash}";
            var assetPath = Path.Combine(objectsDir, hashPrefix, hash);

            // Важно: не только отсутствие файла, но и проверка его SHA1.
            // Иначе можно получить "missing sound" при битом/обрезанном asset файле.
            if (!File.Exists(assetPath) || !HasExpectedSha1(assetPath, hash))
            {
                assetsToDownload.Add((assetUrl, assetPath));
            }
        }

        Console.WriteLine($"Assets to download: {assetsToDownload.Count}, already exist: {totalAssets - assetsToDownload.Count}");
        StatusChanged?.Invoke(this, $"Нужно загрузить: {assetsToDownload.Count} файлов из {totalAssets}");

        if (assetsToDownload.Count == 0)
        {
            StatusChanged?.Invoke(this, "Все ассеты уже загружены!");
            return;
        }

        // Параллельная загрузка (10 потоков)
        int downloaded = 0;
        var semaphore = new System.Threading.SemaphoreSlim(10);
        var tasks = assetsToDownload.Select(async asset =>
        {
            await semaphore.WaitAsync();
            try
            {
                await _downloadService.DownloadFileAsync(asset.url, asset.path);
                var current = System.Threading.Interlocked.Increment(ref downloaded);

                if (current % 50 == 0 || current == assetsToDownload.Count)
                {
                    StatusChanged?.Invoke(this, $"Ассеты: {current}/{assetsToDownload.Count} ({current * 100 / assetsToDownload.Count}%)");
                    Console.WriteLine($"Assets progress: {current}/{assetsToDownload.Count}");
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        StatusChanged?.Invoke(this, $"Все ассеты загружены: {downloaded} новых файлов");
        Console.WriteLine($"Assets complete: {downloaded} downloaded");
    }

    public async Task VerifyAndRepairAssetsAsync()
    {
        try
        {
            var versionJsonPath = Path.Combine(_minecraftDirectory, "versions", VERSION, $"{VERSION}.json");
            if (!File.Exists(versionJsonPath))
            {
                Console.WriteLine("[VerifyAndRepairAssets] version.json not found, skipping");
                return;
            }

            var versionJson = JObject.Parse(File.ReadAllText(versionJsonPath));
            var assetIndex = versionJson["assetIndex"];
            var assetIndexUrl = assetIndex?["url"]?.ToString();
            var assetIndexId = assetIndex?["id"]?.ToString();
            if (string.IsNullOrEmpty(assetIndexUrl) || string.IsNullOrEmpty(assetIndexId))
            {
                Console.WriteLine("[VerifyAndRepairAssets] asset index info is missing, skipping");
                return;
            }

            StatusChanged?.Invoke(this, "Проверка целостности ассетов...");

            var indexesDir = Path.Combine(_minecraftDirectory, "assets", "indexes");
            Directory.CreateDirectory(indexesDir);
            var indexPath = Path.Combine(indexesDir, $"{assetIndexId}.json");
            await _downloadService.DownloadFileAsync(assetIndexUrl, indexPath);

            var indexJson = JObject.Parse(File.ReadAllText(indexPath));
            var objects = indexJson["objects"] as JObject;
            if (objects == null)
            {
                return;
            }

            var objectsDir = Path.Combine(_minecraftDirectory, "assets", "objects");
            var toRepair = new List<(string url, string path)>();

            foreach (var obj in objects.Properties())
            {
                var hash = obj.Value["hash"]?.ToString();
                if (string.IsNullOrEmpty(hash))
                {
                    continue;
                }

                var hashPrefix = hash.Substring(0, 2);
                var assetPath = Path.Combine(objectsDir, hashPrefix, hash);
                if (File.Exists(assetPath) && HasExpectedSha1(assetPath, hash))
                {
                    continue;
                }

                var assetUrl = $"https://resources.download.minecraft.net/{hashPrefix}/{hash}";
                toRepair.Add((assetUrl, assetPath));
            }

            if (toRepair.Count == 0)
            {
                StatusChanged?.Invoke(this, "Ассеты в порядке");
                return;
            }

            StatusChanged?.Invoke(this, $"Восстановление ассетов: {toRepair.Count} файлов...");
            foreach (var item in toRepair)
            {
                await _downloadService.DownloadFileAsync(item.url, item.path);
            }

            StatusChanged?.Invoke(this, "Ассеты восстановлены");
            Console.WriteLine($"[VerifyAndRepairAssets] Repaired assets: {toRepair.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VerifyAndRepairAssets] Error: {ex.Message}");
        }
    }

    private static bool HasExpectedSha1(string filePath, string expectedHashHex)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var sha1 = SHA1.Create();
            var hashBytes = sha1.ComputeHash(stream);
            var actual = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            return string.Equals(actual, expectedHashHex.ToLowerInvariant(), StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private void CreateDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_minecraftDirectory, "versions"));
        Directory.CreateDirectory(Path.Combine(_minecraftDirectory, "libraries"));
        Directory.CreateDirectory(Path.Combine(_minecraftDirectory, "assets"));
        Directory.CreateDirectory(Path.Combine(_minecraftDirectory, "mods"));
    }

    public LaunchOptions CreateLaunchOptions(AuthResult auth)
    {
        var gameDir = _minecraftDirectory;

        PruneConflictingForgeInstallations(gameDir, FORGE_VERSION);

        var installedForgeVersionId = FindInstalledForgeVersionId(gameDir);
        var useForge = !string.IsNullOrWhiteSpace(installedForgeVersionId);
        var versionToUse = useForge ? installedForgeVersionId! : VERSION;
        var versionDir = Path.Combine(gameDir, "versions", versionToUse);

        Console.WriteLine($"[CreateLaunchOptions] Using version: {versionToUse}");
        Console.WriteLine($"[CreateLaunchOptions] Forge available: {useForge}");
        Console.WriteLine($"[CreateLaunchOptions] Version dir: {versionDir}");

        // Читаем правильный AssetIndex из version.json
        var versionJsonPath = Path.Combine(gameDir, "versions", VERSION, $"{VERSION}.json");
        var assetIndexId = "5"; // Дефолт для 1.20.1

        if (File.Exists(versionJsonPath))
        {
            try
            {
                var versionJson = JObject.Parse(File.ReadAllText(versionJsonPath));
                assetIndexId = versionJson["assetIndex"]?["id"]?.ToString() ?? "5";
                Console.WriteLine($"Using asset index: {assetIndexId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not read asset index from version.json: {ex.Message}");
            }
        }

        return new LaunchOptions
        {
            Username = auth.Username,
            UUID = auth.UUID,
            AccessToken = auth.AccessToken,
            Version = versionToUse,
            GameDirectory = gameDir,
            AssetsDirectory = Path.Combine(gameDir, "assets"),
            LibrariesDirectory = Path.Combine(gameDir, "libraries"),
            NativesDirectory = Path.Combine(versionDir, "natives"),
            AssetIndex = assetIndexId,
            MaxMemory = 4096,
            MinMemory = 1024,
            WindowWidth = 1280,
            WindowHeight = 720,
            JavaPath = FindJavaPath(),
            MainClass = useForge ? "cpw.mods.bootstraplauncher.BootstrapLauncher" : "net.minecraft.client.main.Main"
        };
    }

    private static void PruneConflictingForgeInstallations(string gameDir, string expectedForgeBuild)
    {
        try
        {
            var expectedVersionId = $"{VERSION}-forge-{expectedForgeBuild}";
            PruneForgeProfilesExcept(gameDir, expectedVersionId);
            PruneForgeMavenArtifacts(gameDir, expectedForgeBuild);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PruneConflictingForgeInstallations] Warning: {ex.Message}");
        }
    }

    private static void PruneForgeProfilesExcept(string gameDir, string keepVersionId)
    {
        var versionsDir = Path.Combine(gameDir, "versions");
        if (!Directory.Exists(versionsDir))
        {
            return;
        }

        foreach (var dir in Directory.GetDirectories(versionsDir, $"{VERSION}-forge-*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(dir);
            if (string.Equals(name, keepVersionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(dir, true);
                Console.WriteLine($"[PruneForgeProfilesExcept] Removed old Forge profile: {dir}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PruneForgeProfilesExcept] Failed to remove {dir}: {ex.Message}");
            }
        }
    }

    private static void PruneForgeMavenArtifacts(string gameDir, string expectedForgeBuild)
    {
        var forgeRoot = Path.Combine(gameDir, "libraries", "net", "minecraftforge", "forge");
        if (!Directory.Exists(forgeRoot))
        {
            return;
        }

        var expectedPrefix = $"{VERSION}-{expectedForgeBuild}";
        foreach (var dir in Directory.GetDirectories(forgeRoot, $"{VERSION}-*", SearchOption.TopDirectoryOnly))
        {
            var folderName = Path.GetFileName(dir);
            if (string.Equals(folderName, expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(dir, true);
                Console.WriteLine($"[PruneForgeMavenArtifacts] Removed old Forge maven folder: {dir}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PruneForgeMavenArtifacts] Failed to remove {dir}: {ex.Message}");
            }
        }
    }

    private static string? FindInstalledForgeVersionId(string gameDir)
    {
        var versionsDir = Path.Combine(gameDir, "versions");
        if (!Directory.Exists(versionsDir))
        {
            return null;
        }

        var candidates = Directory.GetDirectories(versionsDir, $"{VERSION}-forge-*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Select(name => new
            {
                VersionId = name,
                ForgePart = name.StartsWith($"{VERSION}-forge-", StringComparison.OrdinalIgnoreCase)
                    ? name.Substring($"{VERSION}-forge-".Length)
                    : string.Empty
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.ForgePart))
            .Select(x =>
            {
                var jsonPath = Path.Combine(versionsDir, x.VersionId, $"{x.VersionId}.json");
                return new
                {
                    x.VersionId,
                    x.ForgePart,
                    HasJson = File.Exists(jsonPath)
                };
            })
            .Where(x => x.HasJson)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates
            .OrderByDescending(x => ParseForgeVersion(x.ForgePart))
            .ThenByDescending(x => x.ForgePart, StringComparer.OrdinalIgnoreCase)
            .First();

        return best.VersionId;
    }

    private static Version ParseForgeVersion(string value)
    {
        return Version.TryParse(value, out var parsed)
            ? parsed
            : new Version(0, 0);
    }

    private string FindJavaPath()
    {
        // 1. Проверяем JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var javaExe = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(javaExe))
            {
                Console.WriteLine($"Found Java via JAVA_HOME: {javaExe}");
                return javaExe;
            }
        }

        // 2. Ищем в стандартных местах
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var javaLocations = new[]
        {
            Path.Combine(programFiles, "Java", "jdk-17", "bin", "java.exe"),
            Path.Combine(programFiles, "Java", "jdk-21", "bin", "java.exe"),
            Path.Combine(programFiles, "Java", "jre-17", "bin", "java.exe"),
            Path.Combine(programFiles, "Eclipse Adoptium", "jdk-17.0.12.7-hotspot", "bin", "java.exe"),
            Path.Combine(programFiles, "Eclipse Adoptium", "jdk-21.0.5.11-hotspot", "bin", "java.exe"),
        };

        foreach (var location in javaLocations)
        {
            if (File.Exists(location))
            {
                Console.WriteLine($"Found Java at: {location}");
                return location;
            }
        }

        // 3. Пробуем найти любую Java в Program Files
        var javaDir = Path.Combine(programFiles, "Java");
        if (Directory.Exists(javaDir))
        {
            var jdkDirs = Directory.GetDirectories(javaDir, "jdk*");
            foreach (var dir in jdkDirs)
            {
                var javaExe = Path.Combine(dir, "bin", "java.exe");
                if (File.Exists(javaExe))
                {
                    Console.WriteLine($"Found Java at: {javaExe}");
                    return javaExe;
                }
            }
        }

        Console.WriteLine("WARNING: Java not found, using 'java' and hoping it's in PATH");
        return "java";
    }

    public void ExtractNatives(string version)
    {
        try
        {
            var versionDir = Path.Combine(_minecraftDirectory, "versions", version);
            var nativesDir = Path.Combine(versionDir, "natives");

            // Очищаем папку natives
            if (Directory.Exists(nativesDir))
            {
                Directory.Delete(nativesDir, true);
            }
            Directory.CreateDirectory(nativesDir);

            Console.WriteLine($"Extracting natives to: {nativesDir}");

            // Читаем version.json
            var versionJsonPath = Path.Combine(_minecraftDirectory, "versions", VERSION, $"{VERSION}.json");
            if (!File.Exists(versionJsonPath))
            {
                Console.WriteLine("Warning: version.json not found, skipping natives extraction");
                return;
            }

            var versionJson = JObject.Parse(File.ReadAllText(versionJsonPath));
            var libraries = versionJson["libraries"] as JArray;
            if (libraries == null) return;

            var nativesKey = GetNativesKey();
            var librariesPath = Path.Combine(_minecraftDirectory, "libraries");

            foreach (var library in libraries)
            {
                var downloads = library["downloads"];
                var classifiers = downloads?["classifiers"];
                if (classifiers == null) continue;

                var nativeLib = classifiers[nativesKey];
                if (nativeLib == null) continue;

                var path = nativeLib["path"]?.ToString();
                if (string.IsNullOrEmpty(path)) continue;

                var jarPath = Path.Combine(librariesPath, path);
                if (!File.Exists(jarPath))
                {
                    Console.WriteLine($"Warning: Native library not found: {jarPath}");
                    continue;
                }

                Console.WriteLine($"Extracting: {Path.GetFileName(jarPath)}");

                // Извлекаем содержимое JAR
                using var zipFile = new ZipFile(jarPath);
                foreach (ZipEntry entry in zipFile)
                {
                    if (!entry.IsFile) continue;
                    if (entry.Name.Contains("META-INF")) continue;

                    var entryFileName = Path.GetFileName(entry.Name);
                    var destPath = Path.Combine(nativesDir, entryFileName);

                    using var zipStream = zipFile.GetInputStream(entry);
                    using var fileStream = File.Create(destPath);
                    zipStream.CopyTo(fileStream);
                }
            }

            Console.WriteLine($"Natives extracted successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting natives: {ex.Message}");
        }
    }
}
