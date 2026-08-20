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

    // Named points on the frame axis, drawn in a lane just below the ruler (global, not per-track).
    // A marker with Stop set halts playback when the playhead reaches its frame. Default empty, and
    // absent from JSON written before markers existed, so old documents load unchanged.
    public List<MarkerData> Markers { get; set; } = new();

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

    // The earliest marker carrying a playback action (Stop or Jump) strictly after 'from' and at or
    // before 'to' — a half-open scan of the interval the playhead is about to cross this frame — or
    // null if none. Playback uses it to halt on a stop marker or seek on a jump marker. Plain labelled
    // markers (no action) are ignored. Strictly-after 'from' means the playhead can sit on / resume off
    // an action marker without immediately re-triggering it.
    public MarkerData? EarliestActionMarkerIn(int from, int to)
    {
        MarkerData? best = null;
        foreach (var m in Markers)
            if (m.HasAction && m.Frame > from && m.Frame <= to && (best is null || m.Frame < best.Frame))
                best = m;
        return best;
    }

    // The frame of the marker with the given Id, or -1 if there is no such marker. Used to resolve a
    // jump marker's target (stored by Id so it follows the target as it is moved / renamed).
    public int FrameOfMarker(string id)
    {
        foreach (var m in Markers)
            if (m.Id == id) return m.Frame;
        return -1;
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

// What a track carries. Standard is the original abstract clip (name/colour/fades → domain
// outputs). Audio tracks hold clips that reference a sound file and play through the node's
// internal mixer. Serialized as an int (Standard = 0), so documents written before audio existed
// deserialize to Standard and are unchanged.
public enum TrackKind
{
    Standard = 0,
    Audio = 1,
}

// A horizontal track: a named lane carrying clips that must not overlap in time.
public sealed class TrackData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    // Standard by default so pre-audio documents load unchanged (the field is simply absent there).
    public TrackKind Kind { get; set; } = TrackKind.Standard;
    public List<ClipData> Clips { get; set; } = new();

    // The clip covering a given frame, or null if the frame falls in a gap. Clips never overlap
    // within a track, so at most one qualifies.
    public ClipData? ActiveClipAt(int frame)
    {
        foreach (var c in Clips)
            if (c.Contains(frame)) return c;
        return null;
    }

    // The next clip on this track that starts *after* the given frame (the soonest upcoming clip),
    // or null if there is none — for per-track preloading of what plays next. Doesn't assume the
    // clip list is sorted; picks the smallest Start > frame.
    public ClipData? NextClipAfter(int frame)
    {
        ClipData? best = null;
        foreach (var c in Clips)
            if (c.Start > frame && (best is null || c.Start < best.Start))
                best = c;
        return best;
    }

    // The first clip this track plays inside the range [inFrame, outFrame): the clip active at
    // inFrame if one covers it (playback resumes mid-clip on a loop wrap), otherwise the earliest
    // clip that starts within the range. Null if the track has no clip there.
    public ClipData? FirstClipInRange(int inFrame, int outFrame)
    {
        var active = ActiveClipAt(inFrame);
        if (active is not null) return active;
        ClipData? best = null;
        foreach (var c in Clips)
            if (c.Start >= inFrame && c.Start < outFrame && (best is null || c.Start < best.Start))
                best = c;
        return best;
    }

    // The next clip this track will play while looping over [inFrame, outFrame): the soonest clip
    // starting in (frame, outFrame); if none remain before the loop wraps, the clip the loop replays
    // from its start (FirstClipInRange). This makes "upcoming" wrap around, so a looping timeline
    // always reports something as long as the loop region holds a clip on this track.
    public ClipData? NextClipLooping(int frame, int inFrame, int outFrame)
    {
        ClipData? fwd = null;
        foreach (var c in Clips)
            if (c.Start > frame && c.Start < outFrame && (fwd is null || c.Start < fwd.Start))
                fwd = c;
        return fwd ?? FirstClipInRange(inFrame, outFrame);
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

    // ---- audio clips only (null / default on Standard-track clips) ----
    // Absolute path to the sound file this clip plays. Null on non-audio clips. Stored absolute for
    // v1 (a moved patch breaks the reference); relative-to-patch is a later nice-to-have.
    public string? FilePath { get; set; }
    // Trim in-point: where in the file playback starts, in seconds. The clip plays the file from
    // here at natural rate; the clip's Length (frames → seconds via Fps) is how much of the file
    // sounds. Past the file end = silence. Default 0 = start of the file.
    public double FileInPoint { get; set; }
    // Linear gain applied to this clip's audio (on top of the fade envelope). Default 1 = unity.
    public float Gain { get; set; } = 1f;

    // True when this clip references a sound file (i.e. lives on an audio track).
    [JsonIgnore]
    public bool IsAudio => FilePath is not null;

    // Exclusive end frame; the clip covers [Start, End).
    [JsonIgnore]
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

// A named point on the frame axis, shown in the marker lane below the ruler. Global to the timeline
// (not tied to a track). A marker can carry one playback action as the playhead reaches Frame: Stop
// halts there, or JumpTargetId seeks to another marker and keeps playing (a jump / loop-back). The
// two are mutually exclusive (the editor clears one when you set the other). With neither set it is
// just a labelled cue. Also exposed to the patch as a dynamic-enum entry (name → Frame) so the node's
// Jump input can seek to it. Serialized flat; back-compatible (whole list absent on old docs).
public sealed class MarkerData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Frame { get; set; }
    // When true, the playhead stops on this marker's frame during playback.
    public bool Stop { get; set; }
    // When non-empty, the Id of another marker: on reaching this marker's Frame during playback the
    // playhead seeks to that target and keeps playing. A backward target loops forever until some other
    // event moves the playhead (Stop, an Automata state change, a Set/Jump, Rewind). Mutually exclusive
    // with Stop. Empty (and absent on markers written before this field existed) = no jump.
    public string JumpTargetId { get; set; } = "";
    // Index into the shared clip palette (0..9), same as ClipData.Color. Out-of-range falls back to 0
    // (see InterpolePalette), so markers written before this field existed load as the default blue.
    public int Color { get; set; }

    // True when crossing this marker does something to playback (halts or jumps) rather than just
    // labelling the frame. Not serialized.
    [JsonIgnore] public bool HasAction => Stop || !string.IsNullOrEmpty(JumpTargetId);
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
