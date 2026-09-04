using VL.Interpole.Engine;
using VL.Lib.Collections;

namespace VL.Interpole;

// Splitter for the Interpole node's `Info` bundle that looks a clip up BY NAME and returns its float
// (keyframe) tracks' names and their current values (sampled at the playhead) as two aligned spreads.
// If two clips share the name, the first one found (track-then-clip order) wins. A null/unconnected
// Info, an empty name, or an unknown clip (or one with no float tracks) all yield two empty spreads.
// Same zero-field [ProcessNode] pattern as the other Get… nodes.
[ProcessNode(Name = "GetKeyframes", Category = "Interpole")]
public class GetKeyframesNode
{
    public void Update(InterpoleInfo? info, string clip, out Spread<string> trackNames, out Spread<float> trackValues)
    {
        trackNames = Spread<string>.Empty;
        trackValues = Spread<float>.Empty;
        if (info is null || string.IsNullOrEmpty(clip)) return;
        foreach (var ck in info.ClipKeyframes)
            if (ck.ClipName == clip) { trackNames = ck.TrackNames; trackValues = ck.TrackValues; return; }
    }
}
