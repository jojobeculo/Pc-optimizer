using System.Collections.Concurrent;
using System.Text;

namespace FrameCore.Core;

/// <summary>Minimal local file logger (no telemetry, nothing leaves the machine).</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _file;
    private static readonly ConcurrentQueue<string> Recent = new();
    public static string Process { get; set; } = "ui";

    public static event Action<string>? Written;

    private static string File
    {
        get
        {
            if (_file != null) return _file;
            try
            {
                var dir = AppPaths.Logs;
                // Keep 14 days of logs.
                foreach (var old in Directory.GetFiles(dir, "*.log").Where(f => System.IO.File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-14)))
                    try { System.IO.File.Delete(old); } catch { }
                _file = Path.Combine(dir, $"framecore-{DateTime.Now:yyyy-MM-dd}.log");
            }
            catch { _file = Path.Combine(Path.GetTempPath(), "framecore.log"); }
            return _file;
        }
    }

    public static IEnumerable<string> RecentLines => Recent.ToArray();

    public static void Info(string area, string message) => Write("INFO", area, message);
    public static void Warn(string area, string message) => Write("WARN", area, message);
    public static void Error(string area, string message, Exception? ex = null) =>
        Write("ERROR", area, ex == null ? message : $"{message} | {Describe(ex)}");

    /// <summary>Type and message of the exception and of its inner exceptions, plus the first frames of the original one.</summary>
    public static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e != null && parts.Count < 5; e = e.InnerException) parts.Add($"{e.GetType().Name}: {e.Message}");
        var root = ex; while (root.InnerException != null) root = root.InnerException;
        var frames = (root.StackTrace ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(3).Select(f => f.Trim());
        return string.Join(" ← ", parts) + (frames.Any() ? " @ " + string.Join(" | ", frames) : "");
    }

    private static void Write(string level, string area, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{Process}] [{area}] {message}";
        Recent.Enqueue(line);
        while (Recent.Count > 500 && Recent.TryDequeue(out _)) { }
        try
        {
            lock (Gate) System.IO.File.AppendAllText(File, line + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* logging must never crash the app */ }
        Written?.Invoke(line);
    }
}
