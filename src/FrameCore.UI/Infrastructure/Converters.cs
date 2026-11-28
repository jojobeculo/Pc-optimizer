using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;

namespace FrameCore.UI.Infrastructure;

/// <summary>{l:Loc key} — live-translated text (switches language without restart).</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var b = value switch { bool x => x, null => false, int i => i != 0, string s => !string.IsNullOrWhiteSpace(s), ICollection col => col.Count > 0, _ => true };
        if (p is string s2 && s2 == "invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is Visibility v && v == Visibility.Visible;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is not true;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is not true;
}

/// <summary>RadioButton ↔ string/enum property: IsChecked="{Binding Mode, Converter={StaticResource Eq}, ConverterParameter=Light}".</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => string.Equals(value?.ToString(), p?.ToString(), StringComparison.OrdinalIgnoreCase);
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
    {
        if (value is not true) return Binding.DoNothing;
        if (t.IsEnum) return Enum.Parse(t, p!.ToString()!);
        if (t == typeof(int) || t == typeof(int?)) return int.Parse(p!.ToString()!, CultureInfo.InvariantCulture);
        return p!;
    }
}

public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        (p?.ToString() ?? "").Split('|').Any(x => string.Equals(value?.ToString(), x, StringComparison.OrdinalIgnoreCase)) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Maps a semantic level to a theme brush (foreground or "Soft" background with parameter "soft").</summary>
public sealed class LevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var level = value switch
        {
            Severity s => s switch { Severity.Critical => "Danger", Severity.Warning => "Warning", Severity.Suggestion => "Accent", _ => "Info" },
            Confidence cf => cf switch { Confidence.Confirmed => "Success", Confidence.Probable => "Info", Confidence.Experimental => "Warning", _ => "Danger" },
            RiskLevel r => r switch { RiskLevel.None => "Success", RiskLevel.Low => "Info", RiskLevel.Medium => "Warning", _ => "Danger" },
            TweakStatus st => st switch { TweakStatus.Applied => "Success", TweakStatus.NotApplied => "Neutral", TweakStatus.Partial => "Warning", TweakStatus.Unsupported => "Neutral", TweakStatus.Error => "Danger", _ => "Neutral" },
            StartupClassification sc => sc switch { StartupClassification.Essential => "Success", StartupClassification.Recommended => "Info", StartupClassification.Optional => "Accent", StartupClassification.Risky => "Danger", _ => "Neutral" },
            ChangeSetStatus cs => cs switch { ChangeSetStatus.Applied => "Success", ChangeSetStatus.Completed => "Info", ChangeSetStatus.Restored => "Neutral", ChangeSetStatus.RolledBack => "Warning", ChangeSetStatus.Failed or ChangeSetStatus.PartiallyRestored => "Danger", _ => "Neutral" },
            bool b => b ? "Success" : "Neutral",
            string s2 => s2,
            _ => "Neutral",
        };
        var soft = p is string ps && ps == "soft";
        if (level == "Accent" && soft) return Application.Current.TryFindResource("Brush.AccentSoft") ?? Brushes.Transparent;
        return Application.Current.TryFindResource("Brush." + level + (soft ? "Soft" : "")) ?? Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Enum/string → localized label: ConverterParameter is the key prefix ("confidence.").</summary>
public sealed class LocKeyConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value == null ? "" : Loc.T((p as string ?? "") + value);
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class BytesConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        long l => Format(l), int i => Format(i), double d => Format((long)d), ulong u => Format((long)u), null => Loc.Unknown, _ => value.ToString() ?? "",
    };
    public static string Format(long bytes)
    {
        if (bytes <= 0) return "0";
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        if (Loc.Language == "en") units = new[] { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; var u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return v.ToString(v >= 100 || u == 0 ? "0" : "0.0", CultureInfo.CurrentCulture) + " " + units[u];
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class UnknownConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value == null || (value is string s && string.IsNullOrWhiteSpace(s)) ? Loc.Unknown : (p is string fmt ? string.Format(CultureInfo.CurrentCulture, fmt, value) : value);
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class SeverityIconConverter : IValueConverter
{
    public object? Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        Severity.Critical => Application.Current.TryFindResource("Icon.XCircle"), Severity.Warning => Application.Current.TryFindResource("Icon.Warning"),
        Severity.Suggestion => Application.Current.TryFindResource("Icon.Sparkles"), _ => Application.Current.TryFindResource("Icon.Info"),
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class MultiEqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c) =>
        values.Length == 2 && string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.OrdinalIgnoreCase);
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => t.Select(_ => Binding.DoNothing).ToArray();
}

/// <summary>0-based index → "1.", "2."… (numbered lists).</summary>
public sealed class OneBasedConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is int i ? (i + 1).ToString(c) + "." : "";
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>
/// Available width → number of columns (cards that wrap on small windows): "860" = 2 columns below 860 px, 4 from there;
/// "900:2:5" = 2 columns below 900 px, 5 from there.
/// </summary>
public sealed class WidthToColumnsConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var parts = (p?.ToString() ?? "860").Split(':');
        var limit = double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var l) ? l : 860;
        var small = parts.Length > 1 && int.TryParse(parts[1], out var sm) ? sm : 2;
        var large = parts.Length > 2 && int.TryParse(parts[2], out var lg) ? lg : 4;
        return value is double w && w > 0 && w < limit ? small : large;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Resource key (e.g. "Icon.Chip") → the application resource, for icons chosen by the view model.</summary>
public sealed class ResourceKeyConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is string key && Application.Current?.TryFindResource(key) is { } r ? r : DependencyProperty.UnsetValue;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Width → Visible from the given minimum width ("1250"), Collapsed below (optional decorations on small windows).</summary>
public sealed class MinWidthToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is double w && double.TryParse(p?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var min) && w >= min ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
