using FrameCore.BenchmarkManager.Verification;
using FrameCore.Core;

namespace FrameCore.BenchmarkManager.QuickBench;

/// <summary>Per-machine JSON storage (in %LOCALAPPDATA%\FrameCore, never in the installer).</summary>
public sealed class JsonFolderStore<T> where T : class
{
    private readonly string _dir;
    private readonly Func<T, string> _fileName;
    public JsonFolderStore(string dir, Func<T, string> fileName) { _dir = dir; _fileName = fileName; Directory.CreateDirectory(dir); }

    public void Save(T item) => Json.WriteAtomic(Path.Combine(_dir, _fileName(item)), item);

    public List<T> LoadAll()
    {
        var list = new List<T>();
        foreach (var f in Directory.GetFiles(_dir, "*.json").OrderByDescending(f => f))
        {
            try { if (Json.Read<T>(f) is { } v) list.Add(v); }
            catch (Exception ex) { Log.Warn("Store", $"Unreadable {Path.GetFileName(f)}: {ex.Message}"); }
        }
        return list;
    }

    public void Delete(T item) { try { File.Delete(Path.Combine(_dir, _fileName(item))); } catch (IOException) { } }
}

public static class Stores
{
    public static JsonFolderStore<QuickBenchResult> QuickBench() =>
        new(Path.Combine(AppPaths.Benchmarks, "quick"), r => $"{r.StartedUtc:yyyyMMddHHmmss}-{r.Id}.json");

    public static JsonFolderStore<VerifyRepairReport> Verification() =>
        new(Path.Combine(AppPaths.Root, "Verification"), r => $"{r.StartedUtc:yyyyMMddHHmmss}-{r.Id}.json");
}
