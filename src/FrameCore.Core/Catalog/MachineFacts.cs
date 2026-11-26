using System.Globalization;

namespace FrameCore.Core.Catalog;

/// <summary>
/// Flat dictionary of facts about the machine, used by catalog conditions
/// (e.g. "cpu.isDualCcdX3D", "os.build", "system.isLaptop").
/// </summary>
public sealed class MachineFacts
{
    private readonly Dictionary<string, object?> _facts = new(StringComparer.OrdinalIgnoreCase);

    public object? this[string key]
    {
        get => _facts.TryGetValue(key, out var v) ? v : null;
        set => _facts[key] = value;
    }

    public bool Has(string key) => _facts.TryGetValue(key, out var v) && v != null;
    public IReadOnlyDictionary<string, object?> All => _facts;

    public bool Bool(string key) => this[key] switch
    {
        bool b => b,
        string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1",
        int i => i != 0,
        _ => false,
    };

    public double? Number(string key) => this[key] switch
    {
        null => null,
        int i => i,
        long l => l,
        double d => d,
        float f => f,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    public string? Text(string key) => this[key] switch
    {
        null => null,
        string s => s,
        IEnumerable<string> list => string.Join(",", list),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        var o => o.ToString(),
    };

    public MachineFacts Set(string key, object? value) { _facts[key] = value; return this; }
}
