using System;

namespace ApocalypseLauncher.Core.Models;

public class LaunchOptions
{
    public string Username { get; set; } = string.Empty;
    public string UUID { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string Version { get; set; } = "1.20.1";
    public string GameDirectory { get; set; } = string.Empty;
    public string AssetsDirectory { get; set; } = string.Empty;
    public string LibrariesDirectory { get; set; } = string.Empty;
    public string NativesDirectory { get; set; } = string.Empty;
    public int MaxMemory { get; set; } = 4096;
    public int MinMemory { get; set; } = 1024;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public bool IsFullscreen { get; set; } = false;
    public string JavaPath { get; set; } = "java";
    public string MainClass { get; set; } = "net.minecraft.client.main.Main";
    public string AssetIndex { get; set; } = string.Empty;
}
