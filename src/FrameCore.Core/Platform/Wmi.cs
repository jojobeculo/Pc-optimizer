using System.Management;

namespace FrameCore.Core.Platform;

/// <summary>Defensive WMI/CIM helper: every failure returns an empty result (shown as "Indéterminé").</summary>
public static class Wmi
{
    public const string Cimv2 = @"root\cimv2";
    public const string Storage = @"root\Microsoft\Windows\Storage";
    public const string StandardCimv2 = @"root\StandardCimv2";
    public const string RootWmi = @"root\wmi";
    public const string DeviceGuard = @"root\Microsoft\Windows\DeviceGuard";

    public static List<Dictionary<string, object?>> Query(string wql, string scope = Cimv2, int timeoutSeconds = 15)
    {
        var rows = new List<Dictionary<string, object?>>();
        if (!OperatingSystem.IsWindows()) return rows;
        try
        {
            var options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(timeoutSeconds), ReturnImmediately = true, Rewindable = false };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), options);
            using var results = searcher.Get();
            foreach (ManagementBaseObject mo in results)
            {
                using (mo)
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in mo.Properties) row[p.Name] = p.Value;
                    rows.Add(row);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("WMI", $"{scope}: {wql} → {ex.GetType().Name}: {ex.Message}");
        }
        return rows;
    }

    public static Dictionary<string, object?>? First(string wql, string scope = Cimv2) => Query(wql, scope).FirstOrDefault();

    public static string? Str(this Dictionary<string, object?>? row, string key)
    {
        if (row == null || !row.TryGetValue(key, out var v) || v == null) return null;
        var s = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    public static long? Long(this Dictionary<string, object?>? row, string key)
    {
        if (row == null || !row.TryGetValue(key, out var v) || v == null) return null;
        try { return Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return null; }
    }

    public static int? Int(this Dictionary<string, object?>? row, string key) => row.Long(key) is { } l ? (int)l : null;

    public static bool? Bool(this Dictionary<string, object?>? row, string key)
    {
        if (row == null || !row.TryGetValue(key, out var v) || v == null) return null;
        return v is bool b ? b : Convert.ToInt64(v) != 0;
    }

    public static T[]? Array<T>(this Dictionary<string, object?>? row, string key)
    {
        if (row == null || !row.TryGetValue(key, out var v) || v is not System.Array arr) return null;
        return arr.Cast<object>().Select(o => (T)Convert.ChangeType(o, typeof(T), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    }

    /// <summary>Parses CIM_DATETIME ("yyyymmddHHMMSS.mmmmmmsUUU").</summary>
    public static DateTime? Date(this Dictionary<string, object?>? row, string key)
    {
        var s = row.Str(key);
        if (s == null || s.Length < 8) return null;
        try { return ManagementDateTimeConverter.ToDateTime(s); }
        catch
        {
            return DateTime.TryParseExact(s[..8], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d) ? d : null;
        }
    }

    /// <summary>Invokes a WMI static method (e.g. SystemRestore.CreateRestorePoint).</summary>
    public static Dictionary<string, object?> InvokeStatic(string scope, string className, string method, IDictionary<string, object?> args)
    {
        using var cls = new ManagementClass(new ManagementScope(scope), new ManagementPath(className), null);
        using var inParams = cls.GetMethodParameters(method);
        foreach (var (k, v) in args) inParams[k] = v;
        using var outParams = cls.InvokeMethod(method, inParams, null);
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (outParams != null) foreach (var p in outParams.Properties) result[p.Name] = p.Value;
        return result;
    }
}
