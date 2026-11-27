using FrameCore.Core;
using FrameCore.Core.Localization;
using FrameCore.Core.Platform;

namespace FrameCore.BackupManager;

public sealed record RestorePointResult(bool Created, string Message);

/// <summary>
/// Windows System Restore integration (WMI SystemRestore in root\default, requires administrator rights).
/// Windows limits creation to one point per 24 h by default (SystemRestorePointCreationFrequency):
/// in that case Windows silently skips creation and we report it honestly.
/// </summary>
public static class RestorePointService
{
    private const int ApplicationInstall = 0, ModifySettings = 12, BeginSystemChange = 100;

    public static bool IsSystemRestoreEnabled()
    {
        var reg = new WindowsRegistryAccess();
        var v = reg.GetValue(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "RPSessionInterval");
        if (v != null && RegistryCodec.Encode(v) == "0") return false;
        var disable = reg.GetValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR");
        return disable == null || RegistryCodec.Encode(disable) != "1";
    }

    /// <summary>Must run elevated (called inside the worker).</summary>
    public static RestorePointResult Create(string description)
    {
        if (!AppInfo.IsElevated) return new(false, Loc.T("restorepoint.needsAdmin"));
        try
        {
            var before = LatestSequence();
            var result = Wmi.InvokeStatic(@"root\default", "SystemRestore", "CreateRestorePoint", new Dictionary<string, object?>
            {
                ["Description"] = description.Length > 60 ? description[..60] : description,
                ["RestorePointType"] = ModifySettings,
                ["EventType"] = BeginSystemChange,
            });
            var code = result.Long("ReturnValue") ?? -1;
            if (code != 0)
            {
                // 1058: service disabled; other codes: protection off on the system drive.
                return new(false, Loc.T("restorepoint.failed", code));
            }
            var after = LatestSequence();
            if (after != null && after == before) return new(false, Loc.T("restorepoint.throttled"));
            Log.Info("RestorePoint", $"Created restore point '{description}'");
            return new(true, Loc.T("restorepoint.created"));
        }
        catch (System.Management.ManagementException ex) when (ex.ErrorCode is System.Management.ManagementStatus.NotFound or System.Management.ManagementStatus.InvalidClass or System.Management.ManagementStatus.InvalidNamespace)
        {
            Log.Warn("RestorePoint", "System Restore is not available on this edition: " + ex.Message);
            return new(false, Loc.T("restorepoint.unavailable"));
        }
        catch (Exception ex)
        {
            Log.Error("RestorePoint", "Creation failed", ex);
            return new(false, Loc.T("restorepoint.failed", ex.Message));
        }
    }

    private static long? LatestSequence() =>
        Wmi.Query("SELECT SequenceNumber FROM SystemRestore", @"root\default").Select(r => r.Long("SequenceNumber")).Max();

    public static List<(long Sequence, string Description, DateTime? Created)> List() =>
        Wmi.Query("SELECT SequenceNumber, Description, CreationTime FROM SystemRestore", @"root\default")
           .Select(r => (r.Long("SequenceNumber") ?? 0, r.Str("Description") ?? "", r.Date("CreationTime")))
           .OrderByDescending(x => x.Item1).ToList();
}
