using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace FrameCore.Core.Platform;

/// <summary>Whitelisted Windows tools. Always started from System32 by full path, never through a shell.</summary>
public enum SystemTool { Bcdedit, Defrag, Powercfg, Netsh, Dism, Mdsched, Cleanmgr }

public sealed record ToolResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

public static class SystemTools
{
    static SystemTools()
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
    }

    public static string PathOf(SystemTool tool) => Path.Combine(Environment.SystemDirectory, tool switch
    {
        SystemTool.Bcdedit => "bcdedit.exe",
        SystemTool.Defrag => "defrag.exe",
        SystemTool.Powercfg => "powercfg.exe",
        SystemTool.Netsh => "netsh.exe",
        SystemTool.Dism => "dism.exe",
        SystemTool.Mdsched => "MdSched.exe",
        SystemTool.Cleanmgr => "cleanmgr.exe",
        _ => throw new ArgumentOutOfRangeException(nameof(tool)),
    });

    /// <summary>Runs a whitelisted tool with an argument vector (no shell, no string concatenation of user input).</summary>
    public static ToolResult Run(SystemTool tool, IEnumerable<string> args, TimeSpan? timeout = null)
    {
        foreach (var a in args)
            if (a.IndexOfAny(new[] { '&', '|', '>', '<', '^', '\r', '\n' }) >= 0)
                throw new ArgumentException("Forbidden character in tool argument");

        Encoding oem;
        try { oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } catch { oem = Encoding.UTF8; }

        var psi = new ProcessStartInfo(PathOf(tool))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = oem,
            StandardErrorEncoding = oem,
            WorkingDirectory = Environment.SystemDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Cannot start {tool}");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit((int)(timeout ?? TimeSpan.FromMinutes(2)).TotalMilliseconds))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{tool} timed out");
        }
        Log.Info("Tool", $"{tool} {string.Join(' ', args)} → exit {p.ExitCode}");
        return new ToolResult(p.ExitCode, stdout.Result, stderr.Result);
    }
}
