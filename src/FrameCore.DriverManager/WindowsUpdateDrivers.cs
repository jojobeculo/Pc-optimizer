using FrameCore.Core;
using FrameCore.Core.Localization;

namespace FrameCore.DriverManager;

public sealed class DriverUpdate
{
    public string UpdateId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? DriverModel { get; set; }
    public string? DriverManufacturer { get; set; }
    public string? DriverClass { get; set; }
    public DateTime? DriverDate { get; set; }
    public string? HardwareId { get; set; }
    public string? SupportUrl { get; set; }
    public bool IsDownloaded { get; set; }
}

public sealed class DriverInstallResult
{
    public bool Success { get; set; }
    public bool RebootRequired { get; set; }
    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// Driver updates through the official Windows Update Agent API (Microsoft.Update.Session). Signature and
/// integrity are enforced by Windows Update. Searching works as a standard user; installation needs the worker.
/// </summary>
public static class WindowsUpdateDrivers
{
    private static dynamic Session()
    {
        var t = Type.GetTypeFromProgID("Microsoft.Update.Session") ?? throw new NotSupportedException("Windows Update Agent unavailable");
        dynamic s = Activator.CreateInstance(t)!;
        s.ClientApplicationID = "FrameCore";
        return s;
    }

    public static List<DriverUpdate> Search()
    {
        var list = new List<DriverUpdate>();
        dynamic session = Session();
        dynamic searcher = session.CreateUpdateSearcher();
        dynamic result = searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0");
        dynamic updates = result.Updates;
        int count = updates.Count;
        for (var i = 0; i < count; i++)
        {
            dynamic u = updates.Item(i);
            var d = new DriverUpdate { UpdateId = u.Identity.UpdateID, Title = u.Title, Description = u.Description, IsDownloaded = u.IsDownloaded };
            try { d.DriverModel = u.DriverModel; d.DriverManufacturer = u.DriverManufacturer; d.DriverClass = u.DriverClass; d.HardwareId = u.DriverHardwareID; d.DriverDate = (DateTime)u.DriverVerDate; } catch { }
            try { d.SupportUrl = u.SupportUrl; } catch { }
            list.Add(d);
        }
        Log.Info("WU", $"{list.Count} driver update(s) offered by Windows Update");
        return list;
    }

    /// <summary>Downloads and installs the selected updates (elevated worker only). Only IDs re-found by a fresh search are accepted.</summary>
    public static DriverInstallResult Install(IReadOnlyCollection<string> updateIds)
    {
        var res = new DriverInstallResult();
        if (!AppInfo.IsElevated) { res.Messages.Add(Loc.T("elevation.required")); return res; }
        dynamic session = Session();
        dynamic searcher = session.CreateUpdateSearcher();
        dynamic found = searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0").Updates;
        var collType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!;
        dynamic selected = Activator.CreateInstance(collType)!;
        for (var i = 0; i < (int)found.Count; i++)
        {
            dynamic u = found.Item(i);
            if (updateIds.Contains((string)u.Identity.UpdateID))
            {
                if (!(bool)u.EulaAccepted) u.AcceptEula();
                selected.Add(u);
            }
        }
        if ((int)selected.Count == 0) { res.Messages.Add(Loc.T("drivers.wu.noneSelected")); return res; }
        dynamic downloader = session.CreateUpdateDownloader();
        downloader.Updates = selected;
        downloader.Download();
        dynamic installer = session.CreateUpdateInstaller();
        installer.Updates = selected;
        dynamic result = installer.Install();
        int code = result.ResultCode; // 2 = succeeded, 3 = succeeded with errors
        res.Success = code is 2 or 3;
        res.RebootRequired = result.RebootRequired;
        for (var i = 0; i < (int)selected.Count; i++)
        {
            dynamic r = result.GetUpdateResult(i);
            res.Messages.Add($"{(string)selected.Item(i).Title}: {((int)r.ResultCode is 2 ? "OK" : "code " + (int)r.ResultCode)}");
        }
        Log.Info("WU", $"Install result {code}, reboot={res.RebootRequired}");
        return res;
    }
}
