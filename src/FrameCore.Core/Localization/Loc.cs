using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace FrameCore.Core.Localization;

/// <summary>
/// Flat-key JSON localization (lang/fr.json, lang/en.json). French is the reference language;
/// a missing English key falls back to French, a missing key falls back to the key itself.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    public static string Language => Instance.CurrentLanguage;

    // Copy-on-write: readers (any thread) always see a complete set of tables; writers replace it under a lock.
    private readonly object _writeGate = new();
    private volatile Dictionary<string, Dictionary<string, string>> _tables = new(StringComparer.OrdinalIgnoreCase);
    public string CurrentLanguage { get; private set; } = "fr";
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public IReadOnlyCollection<string> AvailableLanguages => _tables.Keys;

    public void LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file), Json.Options) ?? new();
            LoadTable(Path.GetFileNameWithoutExtension(file), dict);
        }
    }

    public void LoadTable(string language, IDictionary<string, string> entries)
    {
        var table = new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase);
        lock (_writeGate)
            _tables = new Dictionary<string, Dictionary<string, string>>(_tables, StringComparer.OrdinalIgnoreCase) { [language] = table };
    }

    private static CultureInfo? _systemCulture;

    public void SetLanguage(string language)
    {
        CurrentLanguage = language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "fr";
        try
        {
            var system = _systemCulture ??= CultureInfo.CurrentCulture;
            var culture = system.TwoLetterISOLanguageName == CurrentLanguage ? system : new CultureInfo(CurrentLanguage == "en" ? "en-US" : "fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch { /* invariant globalization mode */ }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (_tables.TryGetValue(CurrentLanguage, out var t) && t.TryGetValue(key, out var v)) return v;
        if (_tables.TryGetValue("fr", out var fr) && fr.TryGetValue(key, out var f)) return f;
        return key;
    }

    /// <summary>True when the key exists in the current language or in French (optional texts).</summary>
    public bool Has(string key) =>
        (_tables.TryGetValue(CurrentLanguage, out var t) && t.ContainsKey(key)) || (_tables.TryGetValue("fr", out var fr) && fr.ContainsKey(key));

    /// <summary>Translate with string.Format arguments.</summary>
    public static string T(string key, params object?[] args)
    {
        var s = Instance.Get(key);
        if (args.Length == 0) return s;
        try { return string.Format(CultureInfo.CurrentCulture, s, args); }
        catch (FormatException) { return s; }
    }

    /// <summary>"Indéterminé" / "Undetermined" for unknown values — never invent a value.</summary>
    public static string Unknown => T("common.unknown");

    public static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? Unknown : value;
    public static string Or<TVal>(TVal? value, Func<TVal, string> format) where TVal : struct => value is { } v ? format(v) : Unknown;
}
