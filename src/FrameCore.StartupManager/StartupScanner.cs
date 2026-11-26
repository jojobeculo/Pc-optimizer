using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using FrameCore.Core;
using FrameCore.Core.Catalog;
using FrameCore.Core.Engine;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;
using FrameCore.WindowsOptimization.Platform;

namespace FrameCore.StartupManager;

/// <summary>
/// Autoruns-style inventory: Run/RunOnce keys (64/32-bit, user/machine), Startup folders, logon/boot scheduled tasks,
/// third-party services and Explorer context-menu extensions. Read-only; changes go through <see cref="StartupController"/>.
/// </summary>
public sealed class StartupScanner
{
    private const string CuRun = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string CuRunOnce = @"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string LmRun = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string LmRunOnce = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string LmRun32 = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    public const string CuApproved = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    public const string LmApproved = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    public const string ShellBlocked = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    private readonly IRegistryAccess _reg;
    private readonly ITaskSchedulerAccess _tasks;
    private readonly StartupKnowledgeBase _kb;
    private readonly Dictionary<string, (string? company, string? signer)> _fileCache = new(StringComparer.OrdinalIgnoreCase);

    public StartupScanner(IRegistryAccess reg, ITaskSchedulerAccess tasks, StartupKnowledgeBase kb)
    {
        _reg = reg; _tasks = tasks; _kb = kb;
    }

    public List<StartupEntry> ScanAll(bool includeServices = true, bool includeShellExtensions = true)
    {
        var running = RunningPaths();
        var list = new List<StartupEntry>();
        void Safe(Action a, string what) { try { a(); } catch (Exception ex) { Log.Warn("Startup", $"{what}: {ex.Message}"); } }
        Safe(() => list.AddRange(RunKey(CuRun, CuApproved + @"\Run", StartupKind.RunKey, false)), "HKCU Run");
        Safe(() => list.AddRange(RunKey(LmRun, LmApproved + @"\Run", StartupKind.RunKey, true)), "HKLM Run");
        Safe(() => list.AddRange(RunKey(LmRun32, LmApproved + @"\Run32", StartupKind.RunKey, true)), "HKLM Run32");
        Safe(() => list.AddRange(RunKey(CuRunOnce, null, StartupKind.RunOnce, false)), "HKCU RunOnce");
        Safe(() => list.AddRange(RunKey(LmRunOnce, null, StartupKind.RunOnce, true)), "HKLM RunOnce");
        Safe(() => list.AddRange(Folder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), CuApproved + @"\StartupFolder", false)), "User Startup folder");
        Safe(() => list.AddRange(Folder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), LmApproved + @"\StartupFolder", true)), "Common Startup folder");
        Safe(() => list.AddRange(Tasks()), "Tasks");
        if (includeServices) Safe(() => list.AddRange(Services()), "Services");
        if (includeShellExtensions) Safe(() => list.AddRange(ShellExtensions()), "Shell extensions");
        foreach (var e in list)
        {
            if (e.FilePath != null) e.IsRunning = running.Contains(e.FilePath);
            if (e.Kind == StartupKind.Service && e.Name != null && SafetyPolicy.IsProtectedService(e.Name)) e.Protected = true;
            _kb.Classify(e);
            if (e.Kind == StartupKind.Service && SafetyPolicy.IsProtectedService(e.Name)) { e.Protected = true; e.Classification = StartupClassification.Essential; }
            if (e.Protected) e.CanToggle = false;
        }
        return list;
    }

    private static HashSet<string> RunningPaths()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
            using (p) { try { if (p.MainModule?.FileName is { } f) set.Add(f); } catch { } }
        return set;
    }

    /// <summary>Extracts the executable from a command line (quoted or not, with arguments, with environment variables).</summary>
    public static string? ExtractPath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var c = Environment.ExpandEnvironmentVariables(command.Trim());
        if (c.StartsWith('"'))
        {
            var end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : c.Trim('"');
        }
        var m = Regex.Match(c, @"^(.+?\.(exe|com|bat|cmd|dll|lnk|vbs|ps1|scr))(\s|,|$)", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;
        return c.Split(' ')[0];
    }

    private (string? company, string? signer) FileIdentity(string? path)
    {
        if (path == null) return (null, null);
        if (_fileCache.TryGetValue(path, out var cached)) return cached;
        string? company = null, signer = null;
        try { if (File.Exists(path)) company = FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim(); } catch { }
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (company?.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) == true && path.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase))
        {
            _fileCache[path] = (company, null);
            return (company, null);
        }
        try
        {
            if (File.Exists(path))
            {
#pragma warning disable SYSLIB0057
                using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
                signer = cert.GetNameInfo(X509NameType.SimpleName, false);
            }
        }
        catch { /* not embedded-signed (catalog-signed system files) */ }
        return _fileCache[path] = (string.IsNullOrWhiteSpace(company) ? null : company, signer);
    }

    private static bool IsMicrosoftFile(string? path, string? company, string? signer) =>
        (company?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ?? false)
        || (signer?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ?? false)
        || (path != null && path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase) && company == null && signer == null);

    /// <summary>StartupApproved binary: first byte even (0x02/0x06) = enabled, odd (0x03/0x07) = disabled; absent = enabled.</summary>
    public static bool IsApproved(byte[]? data) => data == null || data.Length == 0 || (data[0] & 1) == 0;

    private IEnumerable<StartupEntry> RunKey(string key, string? approvedKey, StartupKind kind, bool machine)
    {
        foreach (var name in _reg.GetValueNames(key))
        {
            if (string.IsNullOrEmpty(name)) continue;
            var cmd = _reg.GetValue(key, name)?.Data?.ToString();
            var path = ExtractPath(cmd);
            var (company, signer) = FileIdentity(path);
            var approved = approvedKey == null || IsApproved(_reg.GetValue(approvedKey, name)?.Data as byte[]);
            yield return new StartupEntry
            {
                Id = $"{key}|{name}", Kind = kind, Name = name, Command = cmd, FilePath = path, Location = key, Publisher = company, Signer = signer,
                IsMicrosoft = IsMicrosoftFile(path, company, signer), Enabled = approved, ApprovalPath = approvedKey,
                RequiresAdmin = machine, CanToggle = approvedKey != null, FileExists = path != null && File.Exists(path),
            };
        }
    }

    private IEnumerable<StartupEntry> Folder(string folder, string approvedKey, bool machine)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) yield break;
        foreach (var file in Directory.GetFiles(folder))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            var (company, signer) = FileIdentity(file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? null : file);
            yield return new StartupEntry
            {
                Id = $"{folder}|{fileName}", Kind = StartupKind.StartupFolder, Name = Path.GetFileNameWithoutExtension(file), FilePath = file, Command = file,
                Location = folder, Publisher = company, Signer = signer, Enabled = IsApproved(_reg.GetValue(approvedKey, fileName)?.Data as byte[]),
                ApprovalPath = approvedKey, RequiresAdmin = machine, IsMicrosoft = false,
            };
        }
    }

    private IEnumerable<StartupEntry> Tasks()
    {
        foreach (var t in _tasks.Enumerate(includeMicrosoft: false))
        {
            if (!t.RunsAtLogonOrBoot && !Regex.IsMatch(t.Name, "update|updater", RegexOptions.IgnoreCase)) continue;
            var path = ExtractPath(t.Actions.FirstOrDefault());
            var (company, signer) = FileIdentity(path);
            yield return new StartupEntry
            {
                Id = "task|" + t.Path, Kind = StartupKind.ScheduledTask, Name = t.Name, Command = t.Actions.FirstOrDefault(), FilePath = path,
                Location = t.Path, Publisher = company ?? t.Author, Signer = signer, Enabled = t.Enabled, RequiresAdmin = true,
                IsMicrosoft = IsMicrosoftFile(path, company, signer), FileExists = path == null || File.Exists(path),
                Reason = string.Join(", ", t.Triggers),
            };
        }
    }

    private IEnumerable<StartupEntry> Services()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var r in Wmi.Query("SELECT Name, DisplayName, StartMode, State, PathName, Description FROM Win32_Service"))
        {
            var name = r.Str("Name") ?? "";
            var path = ExtractPath(r.Str("PathName"));
            var (company, signer) = FileIdentity(path);
            var isMs = IsMicrosoftFile(path, company, signer) || (path?.StartsWith(windows, StringComparison.OrdinalIgnoreCase) == true && path.Contains("svchost", StringComparison.OrdinalIgnoreCase));
            if (isMs) continue; // Windows services are handled by the curated catalog, never listed for free toggling.
            var mode = r.Str("StartMode");
            var delayed = _reg.GetValue($@"HKLM\SYSTEM\CurrentControlSet\Services\{name}", "DelayedAutostart") is { } d && RegistryCodec.Encode(d) == "1";
            var startType = mode switch { "Auto" => delayed ? "AutomaticDelayed" : "Automatic", "Manual" => "Manual", "Disabled" => "Disabled", _ => mode ?? "?" };
            yield return new StartupEntry
            {
                Id = "svc|" + name, Kind = StartupKind.Service, Name = name, DisplayName = r.Str("DisplayName"), Command = r.Str("PathName"), FilePath = path,
                Location = "Services", Publisher = company, Signer = signer, Enabled = mode is "Auto", StartType = startType, ServiceState = r.Str("State"),
                RequiresAdmin = true, IsMicrosoft = false, FileExists = path == null || File.Exists(path),
            };
        }
    }

    private IEnumerable<StartupEntry> ShellExtensions()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new[] { @"HKCR\*\shellex\ContextMenuHandlers", @"HKCR\Directory\shellex\ContextMenuHandlers", @"HKCR\Directory\Background\shellex\ContextMenuHandlers", @"HKCR\Folder\shellex\ContextMenuHandlers", @"HKCR\Drive\shellex\ContextMenuHandlers" };
        foreach (var root in roots)
        {
            foreach (var sub in _reg.GetSubKeyNames(root))
            {
                var clsid = sub.StartsWith('{') ? sub : _reg.GetValue(root + "\\" + sub, "")?.Data?.ToString();
                if (clsid == null || !clsid.StartsWith('{') || !seen.Add(clsid)) continue;
                var dll = _reg.GetValue($@"HKCR\CLSID\{clsid}\InprocServer32", "")?.Data?.ToString();
                var path = dll != null ? Environment.ExpandEnvironmentVariables(dll.Trim('"')) : null;
                var (company, signer) = FileIdentity(path);
                if (IsMicrosoftFile(path, company, signer)) continue;
                yield return new StartupEntry
                {
                    Id = "shellex|" + clsid, Kind = StartupKind.ShellExtension, Name = sub.StartsWith('{') ? (company ?? clsid) : sub, FilePath = path, Command = path,
                    Location = root, Publisher = company, Signer = signer, Clsid = clsid, RequiresAdmin = true,
                    Enabled = _reg.GetValue(ShellBlocked, clsid) == null, FileExists = path == null || File.Exists(path),
                };
            }
        }
    }
}

/// <summary>Reversible enable/disable of startup entries through the transactional engine (journaled, restorable).</summary>
public sealed class StartupController
{
    private readonly TweakEngine _engine;
    public StartupController(TweakEngine engine) => _engine = engine;

    public static readonly string EnabledBlob = "020000000000000000000000";
    public static readonly string DisabledBlob = "030000000000000000000000";

    public static ActionDefinition? BuildAction(StartupEntry e, bool enable, string? serviceStartType = null)
    {
        switch (e.Kind)
        {
            case StartupKind.RunKey:
            case StartupKind.StartupFolder:
                if (e.ApprovalPath == null) return null;
                var valueName = e.Kind == StartupKind.StartupFolder ? Path.GetFileName(e.FilePath!) : e.Name;
                return new ActionDefinition
                {
                    Type = "registry", Path = e.ApprovalPath, Name = valueName, ValueKind = "Binary",
                    Value = System.Text.Json.JsonSerializer.SerializeToElement(enable ? EnabledBlob : DisabledBlob),
                };
            case StartupKind.ScheduledTask:
                return new ActionDefinition { Type = "task", Path = e.Location, Enabled = enable };
            case StartupKind.Service:
                return new ActionDefinition { Type = "service", Name = e.Name, StartType = enable ? (serviceStartType ?? "Automatic") : (serviceStartType ?? "Manual") };
            case StartupKind.ShellExtension:
                return enable
                    ? new ActionDefinition { Type = "registry", Path = StartupScanner.ShellBlocked, Name = e.Clsid, Delete = true }
                    : new ActionDefinition { Type = "registry", Path = StartupScanner.ShellBlocked, Name = e.Clsid, ValueKind = "String", Value = System.Text.Json.JsonSerializer.SerializeToElement(e.Name) };
            default:
                return null;
        }
    }

    public ApplyResult SetEnabled(StartupEntry e, bool enable, string? serviceStartType = null, string? batchId = null)
    {
        if (e.Protected || !e.CanToggle) return new ApplyResult { Success = false, Error = Loc.T("startup.protected") };
        var action = BuildAction(e, enable, serviceStartType);
        if (action == null) return new ApplyResult { Success = false, Error = Loc.T("startup.notToggleable") };
        var violations = SafetyPolicy.Check(action).ToList();
        if (violations.Count > 0) return new ApplyResult { Success = false, Error = string.Join("; ", violations) };
        var origin = e.Kind == StartupKind.Service ? "service" : "startup";
        var cs = new ChangeSet
        {
            BatchId = batchId, Origin = origin, Category = "Startup",
            Title = new LocalizedText($"{(enable ? "Activer" : "Désactiver")} au démarrage : {e.DisplayName ?? e.Name}", $"{(enable ? "Enable" : "Disable")} at startup: {e.DisplayName ?? e.Name}"),
        };
        var result = _engine.ApplyChangeSet(cs, new[] { action }, new ActionContext(null, origin));
        if (result.Success) e.Enabled = enable;
        return result;
    }
}
