using System.Text.Json;
using System.Text.Json.Serialization;

namespace VL.Interpole.Engine;

// The Interpole document — a simple clip timeline (Premiere-style): a list of tracks,
// each holding non-overlapping clips positioned on an integer frame axis. Persisted on the
// node's 'Data' pin as JSON, saved *inside the .vl patch* (same mechanism as VL.Automata;
// see InterpoleNode). Parse is tolerant: empty/invalid JSON yields a fresh document.
public sealed class InterpoleDocument
{
    public string Title { get; set; } = "Interpole";
    public List<TrackData> Tracks { get; set; } = new();

    // The playhead position in frames (>= 0). Editable by scrubbing in the ruler; persisted so
    // it survives reload and is available for a future Time output / per-track active clip.
    public int Playhead { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static InterpoleDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new InterpoleDocument();
        try { return JsonSerializer.Deserialize<InterpoleDocument>(json, Options) ?? new InterpoleDocument(); }
        catch { return new InterpoleDocument(); }
    }

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    // Short unique id with a type prefix (e.g. "tr-1a2b3c4d").
    public static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];
}

// A horizontal track: a named lane carrying clips that must not overlap in time.
public sealed class TrackData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<ClipData> Clips { get; set; } = new();
}

// A clip occupies the half-open frame range [Start, Start + Length). Length is always >= 1.
public sealed class ClipData
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public int Start { get; set; }
    public int Length { get; set; } = 1;
    // Index into the editor's fixed clip palette (0..9). 0 is the default blue. Out-of-range
    // values fall back to 0 when drawn.
    public int Color { get; set; }
    // Fade lengths in frames from each end: fade-in covers [Start, Start+FadeIn), fade-out covers
    // [End-FadeOut, End). Both >= 0 and FadeIn + FadeOut <= Length (the editor clamps on edit).
    public int FadeIn { get; set; }
    public int FadeOut { get; set; }
}
