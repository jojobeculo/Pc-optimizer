using System.Runtime.InteropServices;
using FrameCore.Core;

namespace FrameCore.BenchmarkManager.Detective;

/// <summary>Raw counters of one process at one instant (from the kernel's process list).</summary>
public sealed record ProcessCounters(int Pid, string Name, int Threads, long CpuTime100ns, long ContextSwitches, long WorkingSetBytes, int SessionId);

/// <summary>CPU use and wake-ups per second of one process over the sampling window.</summary>
public sealed record ProcessActivity(int Pid, string Name, double CpuPercent, double WakeupsPerSec, double MemoryMb, int SessionId, int Threads);

/// <summary>
/// Cost of every running process, including the hundreds of small ones: CPU time and, above all, how often its threads
/// wake the processor (context switches per second). Many tiny wake-ups delay games' threads and keep cores out of
/// their idle states even when each process shows "0 %" in the Task Manager. No administrator rights needed.
/// </summary>
public static class ProcessSampler
{
    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returnLength);
    private const int SystemProcessInformation = 5;

    /// <summary>One snapshot of all processes (x64 layout of SYSTEM_PROCESS_INFORMATION / SYSTEM_THREAD_INFORMATION).</summary>
    public static List<ProcessCounters> Snapshot()
    {
        var list = new List<ProcessCounters>();
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return list;
        var size = 1 << 20;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                var status = NtQuerySystemInformation(SystemProcessInformation, buf, size, out var needed);
                if (status == unchecked((int)0xC0000004)) { size = Math.Max(size * 2, needed + 65536); continue; } // STATUS_INFO_LENGTH_MISMATCH
                if (status != 0) return list;
                var offset = 0;
                while (true)
                {
                    var p = buf + offset;
                    var next = Marshal.ReadInt32(p, 0);
                    var threads = Marshal.ReadInt32(p, 4);
                    var user = Marshal.ReadInt64(p, 40);
                    var kernel = Marshal.ReadInt64(p, 48);
                    var nameLen = Marshal.ReadInt16(p, 56);
                    var namePtr = Marshal.ReadIntPtr(p, 64);
                    var pid = (int)Marshal.ReadInt64(p, 80);
                    var session = Marshal.ReadInt32(p, 100);
                    var ws = Marshal.ReadInt64(p, 144);
                    var name = pid == 0 ? "Idle" : namePtr == IntPtr.Zero ? "System" : Marshal.PtrToStringUni(namePtr, nameLen / 2) ?? "";
                    long switches = 0;
                    for (var t = 0; t < threads; t++) switches += (uint)Marshal.ReadInt32(p, 256 + t * 80 + 64);
                    list.Add(new ProcessCounters(pid, name, threads, user + kernel, switches, ws, session));
                    if (next == 0) break;
                    offset += next;
                }
                return list;
            }
            catch (Exception ex) { Log.Warn("Processes", "Process list unreadable: " + ex.Message); return list; }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return list;
    }

    /// <summary>Samples twice and returns per-process activity over the window (pure delta computation in <see cref="Delta"/>).</summary>
    public static List<ProcessActivity> Measure(TimeSpan window, CancellationToken ct = default)
    {
        var a = Snapshot();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ct.WaitHandle.WaitOne(window);
        var b = Snapshot();
        return Delta(a, b, sw.Elapsed.TotalSeconds, Environment.ProcessorCount);
    }

    public static List<ProcessActivity> Delta(IReadOnlyList<ProcessCounters> before, IReadOnlyList<ProcessCounters> after, double seconds, int cpus)
    {
        var first = before.GroupBy(x => x.Pid).ToDictionary(g => g.Key, g => g.First());
        var result = new List<ProcessActivity>();
        if (seconds <= 0) return result;
        foreach (var b in after)
        {
            if (b.Pid == 0) continue; // the idle "process"
            // A process that started during the window is counted from zero; a reused PID with another name too.
            var a = first.TryGetValue(b.Pid, out var x) && x.Name == b.Name ? x : null;
            var cpu = (b.CpuTime100ns - (a?.CpuTime100ns ?? 0)) / 1e7 / seconds / Math.Max(1, cpus) * 100;
            var wake = Math.Max(0, b.ContextSwitches - (a?.ContextSwitches ?? 0)) / seconds;
            result.Add(new ProcessActivity(b.Pid, b.Name, Math.Max(0, cpu), wake, b.WorkingSetBytes / 1048576.0, b.SessionId, b.Threads));
        }
        return result;
    }
}
