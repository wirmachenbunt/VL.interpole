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

    // The loop region in frames, half-open [LoopIn, LoopOut). Authored via the ribbon above the
    // ruler or the Loop In / Loop Out input pins, and persisted. When the range is unset/invalid
    // (LoopOut <= LoopIn) the effective region falls back to the whole content — see EffectiveLoop.
    public int LoopIn { get; set; }
    public int LoopOut { get; set; }

    // The last frame covered by any clip — the natural end of the timeline (0 if empty).
    public int ContentEnd()
    {
        int end = 0;
        foreach (var tr in Tracks)
            foreach (var c in tr.Clips)
                end = Math.Max(end, c.End);
        return end;
    }

    // The loop region actually used for playback/display: the explicit [LoopIn, LoopOut) when it is
    // a valid non-empty range, otherwise the whole content [0, ContentEnd()). So a document with no
    // loop authored still loops over everything (the pre-loop-region behaviour).
    public (int inFrame, int outFrame) EffectiveLoop()
    {
        int lin = Math.Max(0, LoopIn);
        if (LoopOut > lin) return (lin, LoopOut);
        return (0, ContentEnd());
    }

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

    // The clip covering a given frame, or null if the frame falls in a gap. Clips never overlap
    // within a track, so at most one qualifies.
    public ClipData? ActiveClipAt(int frame)
    {
        foreach (var c in Clips)
            if (c.Contains(frame)) return c;
        return null;
    }
}

// A clip occupies the half-open frame range [Start, Start + Length). Length is always >= 1.
public sealed class ClipData
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public int Start { get; set; }
    public int Length { get; set; } = 1;
    // Index into the fixed clip palette (0..9). 0 is the default blue. Out-of-range values fall
    // back to 0 (see InterpolePalette).
    public int Color { get; set; }
    // Fade lengths in frames from each end: fade-in covers [Start, Start+FadeIn), fade-out covers
    // [End-FadeOut, End). Both >= 0 and FadeIn + FadeOut <= Length (the editor clamps on edit).
    public int FadeIn { get; set; }
    public int FadeOut { get; set; }

    // Exclusive end frame; the clip covers [Start, End).
    public int End => Start + Length;

    // True when the frame lies in the clip's half-open range [Start, End).
    public bool Contains(int frame) => frame >= Start && frame < End;

    // Effective (clamped) fade lengths, robust to out-of-range persisted values. Mirrors the
    // editor's clamp so drawing and evaluation agree: fade-in <= Length, fade-out <= remainder.
    public (int fadeIn, int fadeOut) EffectiveFades()
    {
        int fi = Math.Clamp(FadeIn, 0, Length);
        int fo = Math.Clamp(FadeOut, 0, Length - fi);
        return (fi, fo);
    }

    // Fade gain 0..1 at a frame: 0 outside the clip; ramps up across the fade-in, down across the
    // fade-out, 1 in between — the same triangles the editor draws. Linear, evaluated in frames.
    public float GainAt(int frame)
    {
        if (!Contains(frame)) return 0f;
        var (fi, fo) = EffectiveFades();
        float g = 1f;
        if (fi > 0) g = Math.Min(g, (frame - Start) / (float)fi);
        if (fo > 0) g = Math.Min(g, (End - frame) / (float)fo);
        return Math.Clamp(g, 0f, 1f);
    }
}

// The fixed clip palette — the single source of truth shared by the editor (swatches + clip fill)
// and the node's Active Color output. ClipData.Color is an index into it; out-of-range indices
// fall back to 0 (the original blue), so old documents are unchanged.
public static class InterpolePalette
{
    public static readonly (float R, float G, float B)[] Colors =
    {
        (0.22f, 0.47f, 0.78f), // 0 blue
        (0.24f, 0.62f, 0.60f), // 1 teal
        (0.34f, 0.62f, 0.33f), // 2 green
        (0.70f, 0.64f, 0.24f), // 3 yellow
        (0.82f, 0.54f, 0.24f), // 4 orange
        (0.80f, 0.34f, 0.32f), // 5 red
        (0.75f, 0.36f, 0.56f), // 6 pink
        (0.56f, 0.42f, 0.76f), // 7 purple
        (0.30f, 0.55f, 0.72f), // 8 sky
        (0.46f, 0.50f, 0.57f), // 9 slate
    };

    public static (float R, float G, float B) Rgb(int i)
        => Colors[(uint)i < (uint)Colors.Length ? i : 0];

    public static int Count => Colors.Length;
}
