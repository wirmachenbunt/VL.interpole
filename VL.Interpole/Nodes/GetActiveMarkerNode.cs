using VL.Interpole.Engine;

namespace VL.Interpole;

// Splitter for the Interpole node's one-cable `Info` output (InterpoleInfo) that unpacks just the
// `Active Marker`: the name of the marker the playhead is on this frame, or "" when on none. Chiefly
// for stop markers — while playback is halted on one this holds that marker's name; a plain marker
// shows only for the single frame the playhead sits on / crosses it (a one-frame pulse). `On Marker`
// is the derived convenience (name != ""), handy to gate/trigger downstream without a string compare.
// A dedicated sibling of GetTransport (which also carries this value) for patches that only want the
// marker. Same zero-field [ProcessNode] getter pattern as the other Get… nodes.
[ProcessNode(Name = "GetActiveMarker", Category = "Interpole")]
public class GetActiveMarkerNode
{
    // A null/unconnected input yields "" / false rather than throwing.
    public void Update(InterpoleInfo? info, out string activeMarker, out bool onMarker)
    {
        activeMarker = info?.ActiveMarker ?? "";
        onMarker = !string.IsNullOrEmpty(activeMarker);
    }
}
