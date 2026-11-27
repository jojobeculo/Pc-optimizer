using System.Text.Json;
using System.Text.RegularExpressions;
using FrameCore.Core.Engine;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;

namespace FrameCore.GpuManager;

/// <summary>Windows "Graphics settings" choice for one application (Settings › System › Display › Graphics).</summary>
public enum GpuPreference { Auto = 0, PowerSaving = 1, HighPerformance = 2 }

public sealed record AppGpuPreference(string ExePath, GpuPreference Preference)
{
    public string FileName => Path.GetFileName(ExePath);
}

/// <summary>
/// Per-application GPU preference, written exactly where Windows' own Graphics settings page writes it
/// (HKCU\Software\Microsoft\DirectX\UserGpuPreferences, value = full executable path, data "GpuPreference=N;").
/// Per user, no administrator rights; every change goes through the transactional engine and is restorable.
/// Useful on PCs with two graphics adapters (laptops with an integrated and a dedicated GPU).
/// </summary>
public static class GpuPreferences
{
    public const string Key = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    private const string GlobalSettingsValue = "DirectXUserGlobalSettings";

    public static GpuPreference? Parse(string? data)
    {
        var m = Regex.Match(data ?? "", @"GpuPreference=(\d)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) && n is >= 0 and <= 2 ? (GpuPreference)n : null;
    }

    public static List<AppGpuPreference> Read(IRegistryAccess reg)
    {
        var list = new List<AppGpuPreference>();
        if (!reg.KeyExists(Key)) return list;
        foreach (var name in reg.GetValueNames(Key))
        {
            if (string.Equals(name, GlobalSettingsValue, StringComparison.OrdinalIgnoreCase)) continue;
            if (Parse(reg.GetValue(Key, name)?.Data?.ToString()) is { } p) list.Add(new AppGpuPreference(name, p));
        }
        return list.OrderBy(a => a.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Only real executables are accepted: an absolute path ending in .exe, without wildcards.</summary>
    public static bool IsValidExecutable(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && path.IndexOfAny(new[] { '*', '?', '"', '<', '>', '|' }) < 0;

    public static ActionDefinition Action(string exePath, GpuPreference preference) => new()
    {
        Type = "registry", Path = Key, Name = exePath, ValueKind = "String",
        Value = JsonSerializer.SerializeToElement($"GpuPreference={(int)preference};"),
    };

    /// <summary>Applies (journaled, restorable) the preference for one executable.</summary>
    public static ApplyResult Set(TweakEngine engine, string exePath, GpuPreference preference)
    {
        if (!IsValidExecutable(exePath)) return new ApplyResult { Success = false, Error = Loc.T("gpupref.invalidExe") };
        var file = Path.GetFileName(exePath);
        var cs = new ChangeSet
        {
            Origin = "gpuPreference", Category = "Gpu",
            Title = new LocalizedText(
                $"Carte graphique pour {file} : {Label(preference, "fr")}",
                $"Graphics adapter for {file}: {Label(preference, "en")}"),
        };
        return engine.ApplyChangeSet(cs, new[] { Action(exePath, preference) }, new ActionContext(null, "gpuPreference"));
    }

    private static string Label(GpuPreference p, string lang) => (p, lang) switch
    {
        (GpuPreference.HighPerformance, "fr") => "hautes performances",
        (GpuPreference.PowerSaving, "fr") => "économie d'énergie",
        (_, "fr") => "choix de Windows",
        (GpuPreference.HighPerformance, _) => "high performance",
        (GpuPreference.PowerSaving, _) => "power saving",
        _ => "let Windows decide",
    };
}
