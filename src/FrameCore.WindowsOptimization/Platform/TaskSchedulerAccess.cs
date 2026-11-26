using System.Runtime.InteropServices;

namespace FrameCore.WindowsOptimization.Platform;

public sealed class ScheduledTaskInfo
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public string State { get; set; } = "";
    public string? Author { get; set; }
    public string? Description { get; set; }
    public List<string> Actions { get; set; } = new();
    public List<string> Triggers { get; set; } = new();
    public bool RunsAtLogonOrBoot { get; set; }
    public DateTime? LastRun { get; set; }
    public DateTime? NextRun { get; set; }
    public bool IsMicrosoft => Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);
}

public interface ITaskSchedulerAccess
{
    bool? IsEnabled(string path);
    void SetEnabled(string path, bool enabled);
    IReadOnlyList<ScheduledTaskInfo> Enumerate(bool includeMicrosoft);
}

/// <summary>Task Scheduler 2.0 through its COM automation interface (Schedule.Service).</summary>
public sealed class WindowsTaskSchedulerAccess : ITaskSchedulerAccess
{
    private static dynamic Connect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new NotSupportedException("Task Scheduler unavailable");
        dynamic svc = Activator.CreateInstance(type)!;
        svc.Connect();
        return svc;
    }

    private static (string folder, string name) Split(string path)
    {
        var idx = path.LastIndexOf('\\');
        return idx <= 0 ? ("\\", path.TrimStart('\\')) : (path[..idx], path[(idx + 1)..]);
    }

    private static dynamic? Find(dynamic svc, string path)
    {
        var (folder, name) = Split(path);
        try { return svc.GetFolder(folder).GetTask(name); }
        catch (COMException) { return null; }
        catch (FileNotFoundException) { return null; }
    }

    public bool? IsEnabled(string path)
    {
        var svc = Connect();
        var task = Find(svc, path);
        return task == null ? null : (bool)task.Enabled;
    }

    public void SetEnabled(string path, bool enabled)
    {
        var svc = Connect();
        var task = Find(svc, path) ?? throw new FileNotFoundException($"Scheduled task not found: {path}");
        try { task.Enabled = enabled; }
        catch (UnauthorizedAccessException) { throw; }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070005) { throw new UnauthorizedAccessException(ex.Message, ex); }
    }

    public IReadOnlyList<ScheduledTaskInfo> Enumerate(bool includeMicrosoft)
    {
        var list = new List<ScheduledTaskInfo>();
        try
        {
            var svc = Connect();
            Walk(svc.GetFolder("\\"), list, includeMicrosoft);
        }
        catch (Exception ex) { FrameCore.Core.Log.Warn("Tasks", "Enumeration failed: " + ex.Message); }
        return list;
    }

    private static void Walk(dynamic folder, List<ScheduledTaskInfo> list, bool includeMicrosoft)
    {
        string folderPath = folder.Path;
        if (!includeMicrosoft && folderPath.StartsWith(@"\Microsoft", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            dynamic tasks = folder.GetTasks(1); // TASK_ENUM_HIDDEN
            int count = tasks.Count;
            for (var i = 1; i <= count; i++)
            {
                try { list.Add(Describe(tasks.Item(i))); } catch { }
            }
        }
        catch { }
        try
        {
            dynamic folders = folder.GetFolders(0);
            int n = folders.Count;
            for (var i = 1; i <= n; i++) Walk(folders.Item(i), list, includeMicrosoft);
        }
        catch { }
    }

    private static ScheduledTaskInfo Describe(dynamic t)
    {
        var info = new ScheduledTaskInfo { Path = t.Path, Name = t.Name, Enabled = t.Enabled };
        int state = t.State;
        info.State = state switch { 1 => "Disabled", 2 => "Queued", 3 => "Ready", 4 => "Running", _ => "Unknown" };
        try { info.LastRun = ((DateTime)t.LastRunTime).Year > 2000 ? (DateTime)t.LastRunTime : null; } catch { }
        try { info.NextRun = ((DateTime)t.NextRunTime).Year > 2000 ? (DateTime)t.NextRunTime : null; } catch { }
        try
        {
            dynamic def = t.Definition;
            try { info.Author = def.RegistrationInfo.Author; info.Description = def.RegistrationInfo.Description; } catch { }
            dynamic actions = def.Actions;
            for (var i = 1; i <= (int)actions.Count; i++)
            {
                dynamic a = actions.Item(i);
                if ((int)a.Type == 0) info.Actions.Add(((string)a.Path + " " + (string)(a.Arguments ?? "")).Trim());
            }
            dynamic triggers = def.Triggers;
            for (var i = 1; i <= (int)triggers.Count; i++)
            {
                int type = triggers.Item(i).Type;
                var name = type switch { 0 => "Event", 1 => "Time", 2 => "Daily", 3 => "Weekly", 4 => "Monthly", 6 => "Idle", 7 => "Registration", 8 => "Boot", 9 => "Logon", 11 => "SessionStateChange", _ => "Other" };
                info.Triggers.Add(name);
                if (type is 8 or 9) info.RunsAtLogonOrBoot = true;
            }
        }
        catch { }
        return info;
    }
}

public sealed class InMemoryTaskScheduler : ITaskSchedulerAccess
{
    public Dictionary<string, bool> Tasks { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool? IsEnabled(string path) => Tasks.TryGetValue(path, out var e) ? e : null;
    public void SetEnabled(string path, bool enabled) { if (!Tasks.ContainsKey(path)) throw new FileNotFoundException(path); Tasks[path] = enabled; }
    public IReadOnlyList<ScheduledTaskInfo> Enumerate(bool includeMicrosoft) =>
        Tasks.Select(kv => new ScheduledTaskInfo { Path = kv.Key, Name = kv.Key.Split('\\').Last(), Enabled = kv.Value }).ToList();
}
