using Stride.Core.Mathematics;
using VL.Interpole.Engine;
using VL.Lib.Collections;

namespace VL.Interpole;

// Splitter for the Interpole node's `Info` output (InterpoleInfo) that picks a SINGLE track by name and
// unpacks its "active clip" values as scalars — the per-track equivalent of GetActiveClips, but for one
// named track instead of the whole aligned spreads. `Track` is matched (exact, ordinal) against the
// timeline's track names; the first match wins. A null/unconnected Info, an empty/unknown `Track`, or a
// track whose playhead sits in a gap all yield the same "nothing" values: "" / -1 / -1 / 0 / 0 /
// transparent — so `Clip Length == -1` cleanly signals "no active clip (or no such track)". Sibling of
// the other Get… nodes; same zero-field [ProcessNode] pattern.
[ProcessNode(Name = "GetTrack", Category = "Interpole")]
public class GetTrackNode
{
    public void Update(
        InterpoleInfo? info,
        string track,
        out string activeClipName,
        out int clipFrame,
        out int clipLength,
        out float clipProgress,
        out float fadeGain,
        out Color4 activeColor)
    {
        // Resolve the track by name into an index shared across all the aligned spreads. -1 (not found)
        // falls through to the gap/empty defaults below.
        int i = -1;
        if (info is not null && !string.IsNullOrEmpty(track))
        {
            var tracks = info.TrackNames;
            for (int k = 0; k < tracks.Count; k++)
                if (tracks[k] == track) { i = k; break; }
        }

        activeClipName = At(info?.ActiveClipName, i, "");
        clipFrame = At(info?.ClipFrame, i, -1);
        clipLength = At(info?.ClipLength, i, -1);
        clipProgress = At(info?.ClipProgress, i, 0f);
        fadeGain = At(info?.FadeGain, i, 0f);
        activeColor = At(info?.ActiveColor, i, new Color4(0f, 0f, 0f, 0f));
    }

    // Safe indexer: returns the fallback when the spread is null, the index is out of range, or the
    // track wasn't found (i < 0). Keeps every output on the same "no active clip" story.
    private static T At<T>(Spread<T>? spread, int i, T fallback) =>
        spread is not null && i >= 0 && i < spread.Count ? spread[i] : fallback;
}
