using VL.Interpole.Engine;

namespace VL.Interpole;

// Splitter for the Interpole node's one-cable `Info` output (InterpoleInfo): the transport scalars —
// the playhead `Current Frame`, whether it `Is Playing`, and the `Active Marker` (the marker the
// playhead is on, chiefly the stop marker that halted playback). A zero-field [ProcessNode] getter (it
// holds no state; ProcessNode is just how this project declares a named node in a category), same pattern
// as VL.Automata's Get… nodes. Add sibling Get… nodes the same way to unpack more of InterpoleInfo.
[ProcessNode(Name = "GetTransport", Category = "Interpole")]
public class GetTransportNode
{
    // A null/unconnected input yields 0/false/"" rather than throwing.
    public void Update(InterpoleInfo? info, out int currentFrame, out bool isPlaying, out string activeMarker)
    {
        currentFrame = info?.CurrentFrame ?? 0;
        isPlaying = info?.IsPlaying ?? false;
        activeMarker = info?.ActiveMarker ?? "";
    }
}
