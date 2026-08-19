using VL.Interpole.Engine;
using VL.Lib.Collections;

namespace VL.Interpole;

// Splitter for the Interpole node's `Info` output (InterpoleInfo): per track (aligned by track order),
// the name of that track's next clip to play — for preloading what plays next — or "" if none. Wraps
// around the loop region while looping, so a looping timeline always looks ahead. Sibling of the other
// Get… nodes; same zero-field [ProcessNode] pattern.
[ProcessNode(Name = "GetUpcomingClips", Category = "Interpole")]
public class GetUpcomingClipsNode
{
    // A null/unconnected input yields an empty spread rather than throwing.
    public void Update(InterpoleInfo? info, out Spread<string> upcomingClipNames)
    {
        upcomingClipNames = info?.UpcomingClipNames ?? Spread<string>.Empty;
    }
}
