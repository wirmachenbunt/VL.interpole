using Stride.Core.Mathematics;
using VL.Interpole.Engine;

namespace VL.Interpole;

// Splitter for the Interpole node's `Info` bundle that looks a clip up BY NAME and reports whether it is
// active (the playhead inside its span) plus the same clip values GetTrack exposes — the per-clip twin of
// GetTrack, which instead keys by track and always describes "the active clip on that track".
//
// Resolution is playhead-aware, exactly like GetKeyframes: among clips sharing the name, the one ACTIVE
// under the playhead wins (so a played copy reports its own state, not just the first in document order);
// if two+ are active at once the first wins; if none is active it falls back to the first same-named clip
// — which reports Active = false.
//
// Inactive / not-found use the SAME "no active clip" sentinels as GetTrack ("" / -1 / -1 / 0 / 0 /
// transparent): an inactive clip reads as a gap (strict parity — chosen over leaking its static facts).
// So `Active` tells you if it's playing and `Active Clip Name == ""` (equivalently Clip Length == -1)
// tells you nothing is active for that name this frame. A null/unconnected Info or empty name yields the
// same. Same zero-field [ProcessNode] pattern as the other Get… nodes.
[ProcessNode(Name = "GetClip", Category = "Interpole")]
public class GetClipNode
{
    public void Update(
        InterpoleInfo? info,
        string clip,
        out bool active,
        out string activeClipName,
        out int clipFrame,
        out int clipLength,
        out float clipProgress,
        out float fadeGain,
        out Color4 activeColor)
    {
        // Gap defaults — also the output whenever the clip is inactive or unknown.
        active = false;
        activeClipName = "";
        clipFrame = -1;
        clipLength = -1;
        clipProgress = 0f;
        fadeGain = 0f;
        activeColor = new Color4(0f, 0f, 0f, 0f);
        if (info is null || string.IsNullOrEmpty(clip)) return;

        // Prefer the same-named clip that is active under the playhead; a non-active match is only a
        // fallback (used solely to report Active = false — its values are already the gap sentinels above).
        foreach (var cs in info.Clips)
            if (cs.Name == clip && cs.Active)
            {
                active = true;
                activeClipName = cs.Name;
                clipFrame = cs.ClipFrame;
                clipLength = cs.ClipLength;
                clipProgress = cs.ClipProgress;
                fadeGain = cs.FadeGain;
                activeColor = cs.Color;
                return;
            }
        // No active same-named clip: the gap defaults stand (Active = false).
    }
}
