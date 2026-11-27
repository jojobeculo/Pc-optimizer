using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace FrameCore.Tests;

/// <summary>
/// Static checks of the WPF views, runnable without Windows. A StaticResource that does not exist only fails when the
/// view is first displayed (the whole view then stays empty), so it is caught here instead.
/// </summary>
public class XamlConsistencyTests
{
    private static string Ui => Path.Combine(Repo.Root, "src", "FrameCore.UI");

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(Ui, "*.xaml", SearchOption.AllDirectories)
                 .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                          && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    private static IEnumerable<string> Keys(string file) =>
        Regex.Matches(File.ReadAllText(file), "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value);

    [Fact]
    public void Every_static_resource_is_visible_where_it_is_used()
    {
        var files = XamlFiles().ToList();
        // Application-wide resources: App.xaml and the merged theme dictionaries.
        var app = files.Where(f => Path.GetFileName(f) == "App.xaml" || f.Contains(Path.DirectorySeparatorChar + "Themes" + Path.DirectorySeparatorChar))
                       .SelectMany(Keys).ToHashSet();
        var missing = new List<string>();
        foreach (var f in files)
        {
            var local = Keys(f).ToHashSet();
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{(?:StaticResource|DynamicResource)\s+([^}\s,]+)\}"))
            {
                var key = m.Groups[1].Value;
                if (!local.Contains(key) && !app.Contains(key)) missing.Add($"{Path.GetFileName(f)}: {key}");
            }
        }
        Assert.True(missing.Count == 0, "Unknown resources: " + string.Join(", ", missing.Distinct()));
    }

    [Fact]
    public void Every_localized_key_used_in_the_views_exists_in_both_languages()
    {
        var fr = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Repo.Root, "lang", "fr.json")))!;
        var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Repo.Root, "lang", "en.json")))!;
        var missing = new List<string>();
        foreach (var f in XamlFiles())
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{i:Loc\s+([A-Za-z0-9_.\-]+)\}"))
            {
                var key = m.Groups[1].Value;
                if (!fr.ContainsKey(key) || !en.ContainsKey(key)) missing.Add($"{Path.GetFileName(f)}: {key}");
            }
        Assert.True(missing.Count == 0, "Missing translations: " + string.Join(", ", missing.Distinct()));
    }

    [Fact]
    public void Every_literal_localized_key_used_in_the_code_exists_in_both_languages()
    {
        var fr = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Repo.Root, "lang", "fr.json")))!;
        var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Repo.Root, "lang", "en.json")))!;
        var missing = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Repo.Root, "src"), "*.cs", SearchOption.AllDirectories)
                                   .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"Loc\.(?:T|Instance\.Get)\(\s*""([A-Za-z0-9_.\-]+)""\s*[,)]"))
            {
                var key = m.Groups[1].Value;
                if (!fr.ContainsKey(key) || !en.ContainsKey(key)) missing.Add($"{Path.GetFileName(f)}: {key}");
            }
        Assert.True(missing.Count == 0, "Missing translations: " + string.Join(", ", missing.Distinct()));
    }
}
