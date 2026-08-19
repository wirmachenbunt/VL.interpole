using Stride.Core.Mathematics;
using VL.Interpole.Engine;
using VL.Lib.Collections;

namespace VL.Interpole;

// Splitter for the Interpole node's `Info` output (InterpoleInfo): the per-track "active clip" spreads,
// aligned by track order (index 0 = the top track) — the active clip's name, the playhead's 0-based
// position within it (`Clip Frame`; -1 in a gap), the clip's `Clip Length` (-1 in a gap), the
// normalized `Clip Progress` (0..1; 0 in a gap), the `Fade Gain` (0..1), and the `Active Color`. The
// same values as the main node's per-track pins, unpacked from the one cable. Sibling of the other
// Get… nodes; same zero-field [ProcessNode] pattern.
[ProcessNode(Name = "GetActiveClips", Category = "Interpole")]
public class GetActiveClipsNode
{
    // A null/unconnected input yields empty spreads rather than throwing.
    public void Update(
        InterpoleInfo? info,
        out Spread<string> activeClipName,
        out Spread<int> clipFrame,
        out Spread<int> clipLength,
        out Spread<float> clipProgress,
        out Spread<float> fadeGain,
        out Spread<Color4> activeColor)
    {
        activeClipName = info?.ActiveClipName ?? Spread<string>.Empty;
        clipFrame = info?.ClipFrame ?? Spread<int>.Empty;
        clipLength = info?.ClipLength ?? Spread<int>.Empty;
        clipProgress = info?.ClipProgress ?? Spread<float>.Empty;
        fadeGain = info?.FadeGain ?? Spread<float>.Empty;
        activeColor = info?.ActiveColor ?? Spread<Color4>.Empty;
    }
}
