using System.IO.Compression;
using FrameCore.Core;
using FrameCore.Core.Models;

namespace FrameCore.BackupManager;

/// <summary>
/// Persistent change journal: one JSON file per change set (atomic writes) in %LOCALAPPDATA%\FrameCore\Backups.
/// Each file contains the exact captured "before" state of every action, so every change can be
/// restored individually, by category, by profile batch or all at once — even after a crash.
/// </summary>
public sealed class JsonChangeJournal : IChangeJournal
{
    private readonly string _dir;
    private readonly object _gate = new();
    private readonly Dictionary<string, ChangeSet> _sets = new();
    private readonly Dictionary<string, ChangeBatch> _batches = new();

    public event EventHandler? Changed;

    public JsonChangeJournal(string? directory = null)
    {
        _dir = directory ?? AppPaths.Backups;
        Directory.CreateDirectory(Path.Combine(_dir, "changes"));
        Directory.CreateDirectory(Path.Combine(_dir, "batches"));
        Load();
    }

    public string StorageDirectory => _dir;

    public IReadOnlyList<ChangeSet> ChangeSets { get { lock (_gate) return _sets.Values.OrderByDescending(s => s.CreatedUtc).ToList(); } }
    public IReadOnlyList<ChangeBatch> Batches { get { lock (_gate) return _batches.Values.OrderByDescending(b => b.CreatedUtc).ToList(); } }

    private void Load()
    {
        foreach (var f in Directory.GetFiles(Path.Combine(_dir, "changes"), "*.json"))
        {
            try { if (Json.Read<ChangeSet>(f) is { } cs) _sets[cs.Id] = cs; }
            catch (Exception ex) { Log.Error("Journal", $"Unreadable change set {Path.GetFileName(f)}", ex); }
        }
        foreach (var f in Directory.GetFiles(Path.Combine(_dir, "batches"), "*.json"))
        {
            try { if (Json.Read<ChangeBatch>(f) is { } b) _batches[b.Id] = b; }
            catch (Exception ex) { Log.Error("Journal", $"Unreadable batch {Path.GetFileName(f)}", ex); }
        }
        // A "Pending" set means the app stopped between capture and completion: keep it restorable.
        foreach (var cs in _sets.Values.Where(s => s.Status == ChangeSetStatus.Pending))
        {
            cs.Status = cs.Entries.Any(e => e.Applied) ? ChangeSetStatus.Failed : ChangeSetStatus.RolledBack;
            cs.Error ??= "Interrupted";
            Save(cs);
        }
    }

    public void Save(ChangeSet changeSet)
    {
        lock (_gate)
        {
            _sets[changeSet.Id] = changeSet;
            Json.WriteAtomic(Path.Combine(_dir, "changes", $"{changeSet.CreatedUtc:yyyyMMddHHmmss}-{changeSet.Id}.json"), changeSet);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Save(ChangeBatch batch)
    {
        lock (_gate)
        {
            _batches[batch.Id] = batch;
            Json.WriteAtomic(Path.Combine(_dir, "batches", $"{batch.CreatedUtc:yyyyMMddHHmmss}-{batch.Id}.json"), batch);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ChangeSet? Find(string id) { lock (_gate) return _sets.TryGetValue(id, out var s) ? s : null; }

    /// <summary>Exports the whole journal (and optional extra files) into a zip archive.</summary>
    public void Export(string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        lock (_gate) ZipFile.CreateFromDirectory(_dir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        Log.Info("Journal", $"Exported backups to {zipPath}");
    }

    /// <summary>Imports change sets from an exported archive (existing ids are kept, new ones added).</summary>
    public int Import(string zipPath)
    {
        var count = 0;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            var isChange = entry.FullName.StartsWith("changes", StringComparison.OrdinalIgnoreCase);
            var isBatch = entry.FullName.StartsWith("batches", StringComparison.OrdinalIgnoreCase);
            if (!isChange && !isBatch) continue;
            using var s = entry.Open();
            using var reader = new StreamReader(s);
            var text = reader.ReadToEnd();
            if (isChange)
            {
                var cs = System.Text.Json.JsonSerializer.Deserialize<ChangeSet>(text, Json.Options);
                if (cs == null || Find(cs.Id) != null) continue;
                Save(cs); count++;
            }
            else
            {
                var b = System.Text.Json.JsonSerializer.Deserialize<ChangeBatch>(text, Json.Options);
                if (b == null) continue;
                lock (_gate) if (_batches.ContainsKey(b.Id)) continue;
                Save(b);
            }
        }
        return count;
    }
}
