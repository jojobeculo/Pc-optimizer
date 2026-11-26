using System.Globalization;

namespace FrameCore.BenchmarkManager;

public sealed class PresentMonCapture
{
    public string Application { get; set; } = "";
    public int ProcessId { get; set; }
    public string? Runtime { get; set; }
    public string? PresentMode { get; set; }
    public List<double> FrameTimesMs { get; set; } = new();
    public int DroppedFrames { get; set; }
}

/// <summary>
/// Parses PresentMon CSV files (1.x "MsBetweenPresents", 2.x "MsBetweenPresents"/"FrameTime" columns; "NA" values skipped).
/// Frames are grouped per process; the process with the most frames is the main capture.
/// </summary>
public static class PresentMonCsvParser
{
    public static List<PresentMonCapture> Parse(TextReader reader)
    {
        var header = reader.ReadLine();
        if (header == null) return new();
        var cols = SplitCsv(header).Select(c => c.Trim()).ToList();
        int Col(params string[] names) => names.Select(n => cols.FindIndex(c => c.Equals(n, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(i => i >= 0, -1);
        var app = Col("Application");
        var pid = Col("ProcessID");
        var frame = Col("MsBetweenPresents", "msBetweenPresents", "FrameTime", "MsBetweenAppStart");
        var runtime = Col("Runtime", "PresentRuntime");
        var mode = Col("PresentMode");
        var dropped = Col("Dropped");
        var displayed = Col("DisplayedTime", "MsBetweenDisplayChange");
        if (frame < 0) throw new FormatException("No frame-time column (MsBetweenPresents / FrameTime) found");

        var byProcess = new Dictionary<(string, int), PresentMonCapture>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            var f = SplitCsv(line);
            if (f.Count <= frame) continue;
            var a = app >= 0 && app < f.Count ? f[app] : "";
            var p = pid >= 0 && pid < f.Count && int.TryParse(f[pid], out var parsed) ? parsed : 0;
            if (!byProcess.TryGetValue((a, p), out var cap))
                byProcess[(a, p)] = cap = new PresentMonCapture
                {
                    Application = a, ProcessId = p,
                    Runtime = runtime >= 0 && runtime < f.Count ? f[runtime] : null,
                    PresentMode = mode >= 0 && mode < f.Count ? f[mode] : null,
                };
            if (double.TryParse(f[frame], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) && ms > 0)
                cap.FrameTimesMs.Add(ms);
            if (dropped >= 0 && dropped < f.Count && f[dropped] == "1") cap.DroppedFrames++;
            else if (displayed >= 0 && displayed < f.Count && f[displayed].Equals("NA", StringComparison.OrdinalIgnoreCase)) cap.DroppedFrames++;
        }
        return byProcess.Values.OrderByDescending(c => c.FrameTimesMs.Count).ToList();
    }

    public static List<PresentMonCapture> ParseFile(string path)
    {
        using var r = new StreamReader(path);
        return Parse(r);
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var cur = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"') { quoted = !quoted; continue; }
            if (ch == ',' && !quoted) { result.Add(cur.ToString()); cur.Clear(); continue; }
            cur.Append(ch);
        }
        result.Add(cur.ToString());
        return result;
    }
}
