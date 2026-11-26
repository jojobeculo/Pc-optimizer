using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;

namespace FrameCore.Core.Platform;

public sealed record RegValue(string Kind, object Data);

public interface IRegistryAccess
{
    RegValue? GetValue(string path, string name);
    void SetValue(string path, string name, string kind, object data);
    void DeleteValue(string path, string name);
    bool KeyExists(string path);
    IReadOnlyList<string> GetSubKeyNames(string path);
    IReadOnlyList<string> GetValueNames(string path);
}

/// <summary>Conversion between registry data and the string form stored in snapshots/catalog.</summary>
public static class RegistryCodec
{
    public static string Encode(RegValue v) => v.Kind switch
    {
        "DWord" => unchecked((uint)Convert.ToInt32(v.Data, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture),
        "QWord" => unchecked((ulong)Convert.ToInt64(v.Data, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture),
        "MultiString" => JsonSerializer.Serialize((string[])v.Data),
        "Binary" => Convert.ToHexString((byte[])v.Data),
        _ => Convert.ToString(v.Data, CultureInfo.InvariantCulture) ?? "",
    };

    public static object Decode(string kind, string value) => kind switch
    {
        "DWord" => unchecked((int)ParseUnsigned(value)),
        "QWord" => unchecked((long)ParseUnsigned(value)),
        "MultiString" => value.TrimStart().StartsWith('[') ? JsonSerializer.Deserialize<string[]>(value) ?? Array.Empty<string>() : value.Split('\n'),
        "Binary" => Convert.FromHexString(value.Replace(" ", "").Replace("-", "")),
        _ => value,
    };

    public static ulong ParseUnsigned(string value)
    {
        var s = value.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return ulong.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (s.StartsWith('-')) return unchecked((ulong)long.Parse(s, CultureInfo.InvariantCulture));
        return ulong.Parse(s, CultureInfo.InvariantCulture);
    }

    /// <summary>Normalized comparison (e.g. "0x0000001" == "1" for DWORDs, case-insensitive hex for binary).</summary>
    public static bool AreEqual(string kind, string? a, string? b)
    {
        if (a == null || b == null) return a == b;
        try
        {
            return kind switch
            {
                "DWord" => (uint)ParseUnsigned(a) == (uint)ParseUnsigned(b),
                "QWord" => ParseUnsigned(a) == ParseUnsigned(b),
                "Binary" => string.Equals(a.Replace(" ", ""), b.Replace(" ", ""), StringComparison.OrdinalIgnoreCase),
                "MultiString" => ((string[])Decode(kind, a)).SequenceEqual((string[])Decode(kind, b)),
                _ => string.Equals(a, b, StringComparison.Ordinal),
            };
        }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    public static string KindName(RegistryValueKind k) => k switch
    {
        RegistryValueKind.DWord => "DWord",
        RegistryValueKind.QWord => "QWord",
        RegistryValueKind.ExpandString => "ExpandString",
        RegistryValueKind.MultiString => "MultiString",
        RegistryValueKind.Binary => "Binary",
        _ => "String",
    };

    public static RegistryValueKind ToKind(string k) => k switch
    {
        "DWord" => RegistryValueKind.DWord,
        "QWord" => RegistryValueKind.QWord,
        "ExpandString" => RegistryValueKind.ExpandString,
        "MultiString" => RegistryValueKind.MultiString,
        "Binary" => RegistryValueKind.Binary,
        _ => RegistryValueKind.String,
    };
}

/// <summary>Windows registry, 64-bit view. HKCU can be redirected to HKU\&lt;SID&gt; (used by the elevated worker
/// so it always writes into the profile of the user running the UI, not of the elevating administrator).</summary>
public sealed class WindowsRegistryAccess : IRegistryAccess
{
    /// <summary>When set (worker), HKCU paths are opened under HKEY_USERS\{sid}.</summary>
    public static string? HkcuSidOverride { get; set; }

    private static (RegistryKey root, string sub) Split(string path)
    {
        var p = path.Replace('/', '\\');
        var idx = p.IndexOf('\\');
        var hive = idx < 0 ? p : p[..idx];
        var sub = idx < 0 ? "" : p[(idx + 1)..];
        switch (hive.ToUpperInvariant())
        {
            case "HKLM": case "HKEY_LOCAL_MACHINE":
                return (RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64), sub);
            case "HKCU": case "HKEY_CURRENT_USER":
                if (HkcuSidOverride is { Length: > 0 } sid)
                    return (RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64), sid + "\\" + sub);
                return (RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64), sub);
            case "HKCR": case "HKEY_CLASSES_ROOT":
                return (RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64), sub);
            case "HKU": case "HKEY_USERS":
                return (RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64), sub);
            default: throw new ArgumentException($"Unsupported hive in '{path}'");
        }
    }

    public RegValue? GetValue(string path, string name)
    {
        var (root, sub) = Split(path);
        using (root)
        using (var key = root.OpenSubKey(sub, writable: false))
        {
            if (key == null) return null;
            var data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (data == null) return null;
            return new RegValue(RegistryCodec.KindName(key.GetValueKind(name)), data);
        }
    }

    public void SetValue(string path, string name, string kind, object data)
    {
        var (root, sub) = Split(path);
        using (root)
        using (var key = root.CreateSubKey(sub, writable: true) ?? throw new UnauthorizedAccessException(path))
            key.SetValue(name, data, RegistryCodec.ToKind(kind));
    }

    public void DeleteValue(string path, string name)
    {
        var (root, sub) = Split(path);
        using (root)
        using (var key = root.OpenSubKey(sub, writable: true))
            key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public bool KeyExists(string path)
    {
        var (root, sub) = Split(path);
        using (root)
        using (var key = root.OpenSubKey(sub, false))
            return key != null;
    }

    public IReadOnlyList<string> GetSubKeyNames(string path)
    {
        var (root, sub) = Split(path);
        using (root)
        using (var key = root.OpenSubKey(sub, false))
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> GetValueNames(string path)
    {
        var (root, sub) = Split(path);
        using (root)
        using (var key = root.OpenSubKey(sub, false))
            return key?.GetValueNames() ?? Array.Empty<string>();
    }
}

/// <summary>In-memory registry for tests and simulations.</summary>
public sealed class InMemoryRegistry : IRegistryAccess
{
    private readonly Dictionary<string, Dictionary<string, RegValue>> _keys = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DenyWrites { get; } = new(StringComparer.OrdinalIgnoreCase);

    public RegValue? GetValue(string path, string name) =>
        _keys.TryGetValue(path, out var k) && k.TryGetValue(name, out var v) ? v : null;

    public void SetValue(string path, string name, string kind, object data)
    {
        if (DenyWrites.Contains(path + "\\" + name)) throw new IOException("simulated write failure");
        if (!_keys.TryGetValue(path, out var k)) _keys[path] = k = new(StringComparer.OrdinalIgnoreCase);
        k[name] = new RegValue(kind, data);
    }

    public void DeleteValue(string path, string name) { if (_keys.TryGetValue(path, out var k)) k.Remove(name); }
    public bool KeyExists(string path) => _keys.ContainsKey(path);
    public IReadOnlyList<string> GetSubKeyNames(string path) =>
        _keys.Keys.Where(k => k.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase))
                  .Select(k => k[(path.Length + 1)..].Split('\\')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    public IReadOnlyList<string> GetValueNames(string path) => _keys.TryGetValue(path, out var k) ? k.Keys.ToList() : new List<string>();
}
