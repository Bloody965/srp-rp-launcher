using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace ApocalypseLauncher.Core;

/// <summary>
/// Единая семантическая версия лаунчера для UI и проверки обновлений (совпадает с FileVersion / сборкой).
/// </summary>
public static class LauncherVersionInfo
{
    private static readonly string SemanticVersion = ComputeSemanticVersion();

    public static string GetSemanticVersion() => SemanticVersion;

    private static string ComputeSemanticVersion()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();

            var fromFile = TryReadProductOrFileVersion(asm);
            if (!string.IsNullOrWhiteSpace(fromFile))
                return NormalizeDottedVersion(fromFile);

            var v = asm.GetName().Version;
            if (v != null)
                return FormatAssemblyVersion(v);
        }
        catch
        {
            // ignored
        }

        return "1.0.0";
    }

    private static string? TryReadProductOrFileVersion(Assembly asm)
    {
        var path = asm.Location;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var fvi = FileVersionInfo.GetVersionInfo(path);
        var product = fvi.ProductVersion?.Trim();
        if (!string.IsNullOrEmpty(product))
        {
            var plus = product.IndexOf('+', StringComparison.Ordinal);
            if (plus >= 0)
                product = product[..plus].Trim();
            if (!string.IsNullOrEmpty(product) && char.IsDigit(product[0]))
                return product;
        }

        return fvi.FileVersion?.Trim();
    }

    /// <summary>
    /// Убирает хвост ".0" у четвёртого компонента (1.0.4.0 → 1.0.4), но не схлопывает 1.0.0 → 1.0.
    /// </summary>
    private static string NormalizeDottedVersion(string raw)
    {
        var parts = new List<int>();
        foreach (var segment in raw.Split('.'))
        {
            if (!int.TryParse(segment, out var n))
                return raw.Trim();
            parts.Add(n);
        }

        if (parts.Count == 0)
            return raw.Trim();

        while (parts.Count > 3 && parts[^1] == 0)
            parts.RemoveAt(parts.Count - 1);

        return string.Join(".", parts);
    }

    private static string FormatAssemblyVersion(Version v)
    {
        var major = Math.Max(0, v.Major);
        var minor = Math.Max(0, v.Minor);
        var build = v.Build >= 0 ? v.Build : 0;
        var revision = v.Revision >= 0 ? v.Revision : 0;
        var parts = new List<int> { major, minor, build, revision };
        while (parts.Count > 3 && parts[^1] == 0)
            parts.RemoveAt(parts.Count - 1);
        return string.Join(".", parts);
    }
}
