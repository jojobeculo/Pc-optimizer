using System.Text.Json.Serialization;
using FrameCore.Core.Localization;

namespace FrameCore.Core.Models;

/// <summary>A French/English text pair. French is the initial language of the product.</summary>
public sealed class LocalizedText
{
    [JsonPropertyName("fr")] public string Fr { get; set; } = "";
    [JsonPropertyName("en")] public string En { get; set; } = "";

    public LocalizedText() { }
    public LocalizedText(string fr, string en) { Fr = fr; En = en; }

    public string Get(string language) =>
        language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? (string.IsNullOrWhiteSpace(En) ? Fr : En)
            : (string.IsNullOrWhiteSpace(Fr) ? En : Fr);

    public override string ToString() => Get(Loc.Language);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Fr) && string.IsNullOrWhiteSpace(En);
}
