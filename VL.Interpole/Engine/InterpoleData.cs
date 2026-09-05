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

    // Per-clip float parameter tracks, edited in the clip's keyframe-editor page. For now each is
    // just a named track carrying a single draggable value; keyframe lanes over the clip-local time
    // axis grow from this later. Default empty → old documents and un-animated clips carry none.
    public List<FloatTrackData> FloatTracks { get; set; } = new();

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
    // Total duration of the source file in seconds, captured when the clip is created. Bounds edge
    // resizes so an audio clip can never be stretched past the media it plays: the file window
    // [FileInPoint, FileInPoint + Length/Fps] must stay within [0, FileDurationSeconds]. 0 = unknown
    // (pre-existing docs / not yet probed) → the editor falls back to the decoded peaks' duration.
    public double FileDurationSeconds { get; set; }

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

// A per-clip float parameter track shown in the clip's keyframe-editor page. Currently just a name
// plus a single draggable value — the seed the keyframe lanes will grow from. Serialized flat and
// back-compatible (the whole list is absent on clips authored before this existed).
public sealed class FloatTrackData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    // The constant value used when the track has no keyframes. Once keyframes exist it's ignored (the
    // value is sampled from Keys). Kept for back-compat and as the pre-keyframe default.
    public float Value { get; set; }
    // Keyframes on the clip-local frame axis (frame 0 = the clip's start), kept sorted by Frame. Empty
    // by default → the track is a constant (Value); absent on old documents (back-compat).
    public List<Keyframe> Keys { get; set; } = new();

    // The track's value at a clip-local frame: the constant when there are no keyframes, else the
    // interpolated value between the bracketing keyframes, holding the first/last value outside the
    // keyed range. Interpolation is a cubic Hermite whose endpoint tangents come from the two keys'
    // InterpMode (the After-Effects per-side model): the segment A->B is shaped by A's OUTGOING side
    // and B's INCOMING side — linear side = the straight A->B slope, eased side = zero velocity, auto
    // side = a Catmull-Rom slope through the neighbours; Step holds A's value and jumps at B. All-Linear
    // reduces exactly to the old straight-line interpolation.
    public float SampleAt(int localFrame)
    {
        if (Keys.Count == 0) return Value;
        if (localFrame <= Keys[0].Frame) return Keys[0].Value;
        if (localFrame >= Keys[^1].Frame) return Keys[^1].Value;
        for (int i = 1; i < Keys.Count; i++)
        {
            var b = Keys[i];
            if (localFrame <= b.Frame)
            {
                var a = Keys[i - 1];
                if (b.Frame == a.Frame) return b.Value;
                // Step: the segment holds the left value and jumps at the right key.
                if (a.Interp == InterpMode.Step) return a.Value;

                float dt = b.Frame - a.Frame;
                float t = (localFrame - a.Frame) / dt;
                float secant = b.Value - a.Value;   // value change across the segment (= linear per-t tangent)

                // Outgoing tangent from A's right side.
                float m0 = a.Interp switch
                {
                    InterpMode.EaseOut or InterpMode.EasyEase => 0f,          // eased leave = zero velocity
                    InterpMode.AutoBezier => AutoSlope(i - 1, i - 2, i) * dt, // smooth pass-through at A
                    _ => secant,                                             // Linear / EaseIn = linear leave
                };
                // Incoming tangent into B's left side.
                float m1 = b.Interp switch
                {
                    InterpMode.EaseIn or InterpMode.EasyEase => 0f,           // eased arrival = zero velocity
                    InterpMode.AutoBezier => AutoSlope(i, i - 1, i + 1) * dt, // smooth pass-through at B
                    _ => secant,                                             // Linear / EaseOut / Step = linear arrive
                };

                // Cubic Hermite basis. With m0 == m1 == secant this collapses to the straight line.
                float t2 = t * t, t3 = t2 * t;
                float h00 = 2f * t3 - 3f * t2 + 1f;
                float h10 = t3 - 2f * t2 + t;
                float h01 = -2f * t3 + 3f * t2;
                float h11 = t3 - t2;
                return h00 * a.Value + h10 * m0 + h01 * b.Value + h11 * m1;
            }
        }
        return Keys[^1].Value;
    }

    // Central-difference slope (value per frame) at key 'center' using the neighbours 'leftIdx' and
    // 'rightIdx'; falls back to the one-sided slope toward whichever neighbour exists when the other is
    // out of range (so an auto tangent at the first/last key degrades to the plain segment slope). Used
    // for the AutoBezier tangents.
    private float AutoSlope(int center, int leftIdx, int rightIdx)
    {
        int lo = leftIdx >= 0 ? leftIdx : center;
        int hi = rightIdx < Keys.Count ? rightIdx : center;
        float df = Keys[hi].Frame - Keys[lo].Frame;
        return df == 0f ? 0f : (Keys[hi].Value - Keys[lo].Value) / df;
    }

    // Create the keyframe at a clip-local frame, or update its value if one already sits there. Keeps
    // Keys sorted by Frame. Returns the keyframe (new or existing).
    public Keyframe SetKeyframe(int localFrame, float value)
    {
        for (int i = 0; i < Keys.Count; i++)
        {
            if (Keys[i].Frame == localFrame) { Keys[i].Value = value; return Keys[i]; }
            if (Keys[i].Frame > localFrame) { var k = Keyframe.New(localFrame, value); Keys.Insert(i, k); return k; }
        }
        var kn = Keyframe.New(localFrame, value);
        Keys.Add(kn);
        return kn;
    }
}

// Per-keyframe interpolation type — the After-Effects per-side model. A segment between two keys is
// shaped by the LEFT key's OUTGOING side and the RIGHT key's INCOMING side; each value below is a
// preset (incoming, outgoing) pair (see FloatTrackData.SampleAt for the maths):
//   Linear      in/out linear — a straight line (the original behaviour)
//   Step        out = hold: the segment holds the left value and jumps at the right key
//   EaseIn      incoming eased (slow arrival), outgoing linear
//   EaseOut     incoming linear, outgoing eased (slow departure)
//   EasyEase    both sides eased (slow arrival and departure)
//   AutoBezier  both sides auto-smoothed (Catmull-Rom slope through the key)
// Serialized as an int with Linear = 0, so keyframes written before easing existed load as Linear and
// behave exactly as before.
public enum InterpMode
{
    Linear = 0,
    Step = 1,
    EaseIn = 2,
    EaseOut = 3,
    EasyEase = 4,
    AutoBezier = 5,
}

// One keyframe on a float track: a value at a clip-local frame (frame 0 = the clip's start), plus its
// interpolation type. Id is stable across edits so the editor can hold a selection of keyframes (empty
// on old data → the editor backfills one).
public sealed class Keyframe
{
    public string Id { get; set; } = "";
    public int Frame { get; set; }
    public float Value { get; set; }
    // How this key interpolates to its neighbours (see InterpMode). Default Linear (= 0), so keyframes
    // written before easing existed load as plain linear — behaviour unchanged.
    public InterpMode Interp { get; set; } = InterpMode.Linear;

    public static Keyframe New(int frame, float value) =>
        new() { Id = InterpoleDocument.NewId("kf"), Frame = frame, Value = value };
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
