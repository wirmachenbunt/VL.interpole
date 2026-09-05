using VL.Interpole.Engine;
using VL.Lib.Collections;

namespace VL.Interpole;

// Splitter for the Interpole node's `Info` bundle that looks a clip up BY NAME and returns its float
// (keyframe) tracks' names and their current values (sampled at the playhead) as two aligned spreads.
// Resolution is playhead-aware: the same-named clip that is ACTIVE under the playhead wins (so a played
// copy of a clip animates, not just the first one in document order); if two+ same-named clips are
// active at once the first wins; if none is active it falls back to the first same-named clip's held
// value. A null/unconnected Info, an empty name, or an unknown clip (or one with no float tracks) all
// yield two empty spreads. Same zero-field [ProcessNode] pattern as the other Get… nodes.
[ProcessNode(Name = "GetKeyframes", Category = "Interpole")]
public class GetKeyframesNode
{
    public void Update(InterpoleInfo? info, string clip, out Spread<string> trackNames, out Spread<float> trackValues)
    {
        trackNames = Spread<string>.Empty;
        trackValues = Spread<float>.Empty;
        if (info is null || string.IsNullOrEmpty(clip)) return;
        ClipKeyframeSnapshot? fallback = null;
        foreach (var ck in info.ClipKeyframes)
            if (ck.ClipName == clip)
            {
                if (ck.Active) { trackNames = ck.TrackNames; trackValues = ck.TrackValues; return; }  // active clip wins
                fallback ??= ck;   // first same-named clip → used only if none is active
            }
        if (fallback is not null) { trackNames = fallback.TrackNames; trackValues = fallback.TrackValues; }
    }
}
