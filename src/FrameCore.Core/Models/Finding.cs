namespace FrameCore.Core.Models;

/// <summary>A diagnostic result. Text is produced in the current UI language.</summary>
public sealed class Finding
{
    public string Id { get; set; } = "";
    /// <summary>Cpu, Gpu, Ram, Bios, Storage, Display, Network, Windows, Startup, Drivers, Security, Power</summary>
    public string Category { get; set; } = "Windows";
    public Severity Severity { get; set; }
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string? Recommendation { get; set; }
    /// <summary>Under which conditions the measurement was taken (e.g. "idle desktop", "during 60 s capture of game.exe").</summary>
    public string? Context { get; set; }
    public Confidence Confidence { get; set; } = Confidence.Confirmed;
    public List<string> RelatedTweakIds { get; set; } = new();
    public List<string> Steps { get; set; } = new();
    public List<LinkInfo> Links { get; set; } = new();
    /// <summary>Optional UI action identifier (e.g. "display.maxRefresh", "open:windowsdefender://coreisolation").</summary>
    public string? ActionId { get; set; }
    public string? ActionLabel { get; set; }
}

public sealed record LinkInfo(string Label, string Url);
