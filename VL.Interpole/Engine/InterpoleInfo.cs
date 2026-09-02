using Stride.Core.Mathematics;
using VL.Lib.Collections;

namespace VL.Interpole.Engine;

// A one-cable snapshot of everything the running node reports about the timeline this frame: the same
// values that come out of the individual output pins (Current Frame, Is Playing, and the per-track
// aligned spreads), bundled into a single immutable object so downstream patches can pass "the whole
// timeline state" on one wire and unpack only what they need (mirrors VL.Automata's AutomataInfo).
//
// It's a *snapshot*, not a live handle to the editor/engine: a fresh (cheap) record is built each frame
// from the values the node has already computed, so it's safe to hold, compare, or route without ever
// touching the mutable document. The scalars are copied; the spreads are the node's already-built
// spreads (themselves immutable), shared by reference — no per-frame copying of the per-track lists.
//
// Audio is deliberately NOT bundled here: it's a live signal bus wired straight into VL.Audio, not
// information to route/unpack, so it stays on its own `Audio` pin.
public sealed record InterpoleInfo(
    // The playhead position in frames — the current time on the timeline.
    int CurrentFrame,
    // True while the timeline is playing (Play pressed, not yet stopped / reached the end).
    bool IsPlaying,
    // The name of the marker the playhead is on, or "" when on none. Chiefly for stop markers: while
    // playback is halted on one it holds that marker's name; a plain marker shows only for the single
    // frame the playhead sits on / crosses it (a one-frame pulse).
    string ActiveMarker,
    // Per track (aligned by track order): the track's name — the key for looking one track out of the
    // aligned spreads by name (see the GetTrack node). Index i is the same track across every spread here.
    Spread<string> TrackNames,
    // Per track (aligned by track order): the active clip's label, or "" where the playhead is in a gap.
    Spread<string> ActiveClipName,
    // Per track: the playhead's 0-based frame position within the active clip, or -1 in a gap.
    Spread<int> ClipFrame,
    // Per track: the active clip's total length in frames, or -1 in a gap.
    Spread<int> ClipLength,
    // Per track: the playhead's position within the active clip normalized to 0..1 (0 at its first
    // frame, 1 at its last), or 0 in a gap.
    Spread<float> ClipProgress,
    // Per track: the active clip's fade envelope at the playhead (0..1), or 0 in a gap.
    Spread<float> FadeGain,
    // Per track: the active clip's palette colour, or transparent (alpha 0) in a gap.
    Spread<Color4> ActiveColor,
    // Per track: the name of that track's next clip to play (for preloading), or "" if none; wraps
    // around the loop region while looping.
    Spread<string> UpcomingClipNames)
{
    // A harmless empty snapshot (e.g. before the first Update, or a null/unconnected input).
    public static readonly InterpoleInfo Empty = new(
        0, false, "",
        Spread<string>.Empty, Spread<string>.Empty, Spread<int>.Empty, Spread<int>.Empty,
        Spread<float>.Empty, Spread<float>.Empty, Spread<Color4>.Empty, Spread<string>.Empty);
}
