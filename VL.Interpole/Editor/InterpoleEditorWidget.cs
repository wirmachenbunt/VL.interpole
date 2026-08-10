using System.Globalization;
using System.Numerics;
using ImGuiNET;
using VL.Interpole.Engine;
using VL.ImGui;
using VL.ImGui.Widgets;

// Inside namespace VL.Interpole the bare name "ImGui" would bind to the sibling namespace
// VL.ImGui; alias it to the ImGui.NET static class we actually call (same gotcha as the
// VL.Automata editor).
using IG = ImGuiNET.ImGui;
using ImGuiInputTextFlags = ImGuiNET.ImGuiInputTextFlags;

namespace VL.Interpole;

// The Interpole clip-timeline editor, hand-drawn with ImGui's DrawList and exposed as a
// VL.ImGui.Widget. The node wraps it into a Skia layer (ToSkiaLayer) rendered in its own
// window — the setup proven in VL.Automata (full-screen borderless window, DIPTopLeft space,
// optional transparent background).
//
// Layout (Premiere-style):
//   ┌──────────┬───────────────── ruler (frames) ─────────────┐
//   │ + Track  │  0    30    60    90   ...                    │
//   ├──────────┼───────────────────────────────────────────────┤
//   │ Track 1  │ [ clip 1 ][   clip 2   ]                       │
//   │ Track 2  │              [ clip ]                          │
//   └──────────┴───────────────────────────────────────────────┘
// Left: fixed-width track-header column. Right: clip lanes on an integer frame axis.
//
// Interactions:
//   • "+ Track" button / double-click empty header → add track
//   • double-click a track header → rename it (inline)
//   • Alt or Middle click a track header → delete the track (and its clips)
//   • double-click empty lane → add a clip there (clamped to not overlap)
//   • drag a clip body → move it in time (clamped to its neighbours)
//   • drag a clip's left/right edge → resize it (clamped, min length 1)
//   • double-click a clip → rename it (inline)
//   • Alt or Middle click a clip → delete it
//   • mouse wheel over the lanes → zoom the time axis (around the cursor)
//   • right-drag → pan the view (time and tracks)
//
// Clips never overlap within a track: all move/resize/add operations clamp against the
// clip's immediate neighbours (see LeftBound / RightBound / AddClip).
public sealed class InterpoleEditorWidget : IDisposable
{
    // ---- layout constants (screen pixels / frames) ----
    private const float HeaderWidth = 240f;   // track-name column
    private const float TransportHeight = 88f;// transport toolbar (playhead readout + buttons)
    private const float RulerHeight = 64f;    // frame ruler strip (tall enough for the labels)
    private const float TrackHeight = 54f;    // one lane
    private const float ClipPadY = 6f;        // vertical inset of a clip within its lane
    private const float ClipRounding = 4f;    // corner radius of a clip's rounded rect
    private const float EdgeGrabPx = 6f;      // width of a clip's resize handle
    private const float FadeHandlePx = 11f;   // size of a clip's top-corner fade handle
    private const int DefaultClipLen = 30;    // frames, for a freshly added clip
    private const float MinPxPerFrame = 0.5f, MaxPxPerFrame = 40f;
    private const float ReadoutTextScale = 1f; // playhead/timecode readout size, x the base font
    private const float InspectorWidth = 280f;   // right-side clip inspector panel
    private const float ToggleStripPx = 18f;     // inspector open/close strip at the far right
    private const float ReadoutPadX = 24f;     // left inset of the readout from the bar edge

    private readonly string _title;
    private int _trackCounter;
    private int _clipCounter;

    public InterpoleDocument Document { get; }
    // When false the canvas is drawn transparent — both the explicit fill AND ImGui's own
    // window background are skipped — so whatever is behind the renderer shows through.
    public bool ShowBackground = true;
    // Frames per second used to derive timecode for the ruler + readout (the "time / frames"
    // toggle). Set from the node's inspector-only Fps pin each frame; clamped >= 1 by the setter.
    private int _fps = 60;
    public int Fps { get => _fps; set => _fps = Math.Max(1, value); }
    // Playback timing mode, set from the node's hidden pin. Default (false) advances the playhead
    // by real elapsed time × Fps, so one wall-clock second covers Fps frames (1s = 1s) regardless
    // of render rate. When true, restores the legacy behaviour: one document-frame per rendered
    // frame (bound to the renderer's FPS). Independent of the ruler's time/frames display toggle.
    public bool FrameStepPlayback;
    // Raised when an edit mutates the document, so the node persists it to the Data pin.
    public Action? Changed;
    public Widget Widget { get; }

    // ---- view transform (time axis) ----
    private float _pxPerFrame = 6f;
    private float _scrollX;   // pixels scrolled along time, >= 0
    private float _scrollY;   // pixels scrolled through the track stack, >= 0

    // ---- per-frame cached screen geometry (set at the top of Draw) ----
    private Vector2 _origin;
    private Vector2 _size;
    private float _laneLeft;  // x where the clip lanes begin (= _origin.X + HeaderWidth)
    private float _rulerTop;  // y where the ruler begins (= _origin.Y + TransportHeight)
    private float _lanesTop;  // y where the lanes begin (= _rulerTop + RulerHeight)

    // ---- interaction / drag state ----
    private enum DragMode { None, MoveClip, ResizeL, ResizeR, FadeIn, FadeOut, Playhead, Marquee }
    private DragMode _drag;
    private string? _dragClipId;
    private float _dragGrabFrame;   // MoveClip: (mouseFrame - clip.Start) captured at grab
    private bool _panning;
    private string? _hoverClipId;
    private string? _selectedClipId;  // primary selection: shown in the inspector; the drag target
    // Full multi-selection (marquee or Shift/Ctrl-click). Contains _selectedClipId when non-null.
    // Highlighted clips + the copy source. Ids, not references, so it survives the document rebuilds
    // that undo/redo do (clip Ids are stable across serialization).
    private readonly HashSet<string> _selection = new();
    private bool _playheadSnapped;   // set while a Shift-scrub is snapped, for playhead feedback

    // ---- marquee (rubber-band) selection ----
    private Vector2 _marqueeAnchor;               // fixed corner (screen space), set on press
    private HashSet<string> _marqueeBase = new();  // selection to union with (additive marquee)

    // ---- clip clipboard (copy / paste) ----
    // A copied clip plus where it sat, so a multi-clip paste can rebuild the group at the playhead:
    // TrackIndex = the track it came from; StartOffset = frames after the earliest copied clip.
    private readonly List<ClipCopy> _clipboard = new();
    private sealed record ClipCopy(ClipData Clip, int TrackIndex, int StartOffset);

    // ---- inspector panel (right side) ----
    private bool _inspectorOpen;   // collapsed by default; open via the far-right strip
    private float _canvasRight;        // right edge of the timeline area (= inspector/strip left)
    private float _stripLeft;          // left edge of the open/close strip at the far right
    private string _inspName = "";     // name-field buffer for the inspector
    private string? _inspNameId;       // clip the buffer belongs to (re-seed on selection change)
    private bool _inspNameActive;      // the name field is being typed in (don't re-seed then)

    // ---- Shift-snapping (playhead scrub + clip move/resize) ----
    private const float SnapPx = 8f;                 // snap radius in screen pixels (zoom-independent)
    private readonly List<int> _snapTargets = new(); // reused each frame to avoid allocation
    private bool _clipSnapActive;                    // a clip drag is currently snapped (draws a guide)
    private int _clipSnapFrame;                       // frame the clip drag snapped to

    // ---- transport / playback (session-transient; the playhead itself is in the document) ----
    private enum TButton { Play = 0, Stop = 1, Rewind = 2, Loop = 3 }
    private bool _playing;
    private bool _loop;
    private float _playAccum;   // fractional-frame accumulator for real-time (delta) playback
    // Ruler + playhead readout show timecode (M:SS[:FF] via Fps) instead of raw frame numbers.
    // A view preference only — session-transient, like _playing / _loop; not saved in the document.
    // Defaults to timecode (the stopwatch toggle on).
    private bool _showTimecode = true;

    // ---- inline editing ----
    private enum EditKind { None, TrackName, ClipLabel }
    private EditKind _editKind;
    private string _editId = "";
    private string _editBuffer = "";
    private bool _focusEdit;

    // ---- undo / redo ----
    // In-editor history of *structural* document snapshots (JSON). The Data-pin autosave writes
    // with ~AddToHistory (it deliberately stays out of vvvv's undo stack, so continuous autosave
    // doesn't spam it), so the editor keeps its own. Snapshots exclude the playhead (see
    // StructuralSnapshot): the live playhead is always preserved across undo/redo, so history
    // never fights the transport.
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private string _baseline = "";   // structural snapshot as of the last committed edit
    private const int MaxUndo = 128;

    public InterpoleEditorWidget(string title, InterpoleDocument doc)
    {
        _title = title;
        Document = doc;
        _trackCounter = doc.Tracks.Count;
        _clipCounter = doc.Tracks.Sum(t => t.Clips.Count);
        Widget = new ImmediateModeWidget { Updator = Draw };
        _baseline = StructuralSnapshot();
    }

    private static uint Col(float r, float g, float b, float a = 1f)
        => IG.GetColorU32(new Vector4(r, g, b, a));

    // Fixed 10-colour clip palette; a clip stores an index into it (ClipData.Color). Index 0 is
    // the original blue so existing clips keep their look.
    private static readonly (float r, float g, float b)[] ClipPalette =
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

    private static (float r, float g, float b) ClipRgb(int i)
        => ClipPalette[(uint)i < (uint)ClipPalette.Length ? i : 0];

    // ---- time / geometry helpers ----
    private float FrameToX(float f) => _laneLeft + f * _pxPerFrame - _scrollX;
    private float XToFrame(float x) => (x - _laneLeft + _scrollX) / _pxPerFrame;
    private float LaneRight => _canvasRight;
    private float LanesBottom => _origin.Y + _size.Y;
    private float TrackTop(int i) => _lanesTop + i * TrackHeight - _scrollY;
    private static int ClipEnd(ClipData c) => c.Start + c.Length;

    // ============================== draw ==============================

    private void Draw(Context context)
    {
        var io = IG.GetIO();
        Vector2 display = io.DisplaySize;
        IG.SetNextWindowPos(Vector2.Zero);
        IG.SetNextWindowSize(display);
        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBringToFrontOnFocus;
        if (!ShowBackground) flags |= ImGuiWindowFlags.NoBackground;

        IG.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        bool open = IG.Begin("Interpole — " + _title, flags);
        IG.PopStyleVar();
        if (!open) { IG.End(); return; }

        _origin = IG.GetCursorScreenPos();
        _size = IG.GetContentRegionAvail();
        if (_size.X < 64) _size.X = 64;
        if (_size.Y < 64) _size.Y = 64;
        _laneLeft = _origin.X + HeaderWidth;
        _rulerTop = _origin.Y + TransportHeight;
        _lanesTop = _rulerTop + RulerHeight;
        // The timeline area ends at _canvasRight; the open/close strip lives in the far-right
        // ToggleStripPx column, and the inspector panel (when open) between them.
        _stripLeft = _origin.X + _size.X - ToggleStripPx;
        _canvasRight = MathF.Max(_laneLeft + 120f, _stripLeft - (_inspectorOpen ? InspectorWidth : 0f));

        // Allow the inspector's widgets (submitted later) to sit over the full-canvas InvisibleButton.
        IG.SetNextItemAllowOverlap();
        IG.InvisibleButton("interpole-canvas", _size);
        bool hovered = IG.IsItemHovered();
        var dl = IG.GetWindowDrawList();
        Vector2 mouse = IG.GetMousePos();

        ClampScroll();

        // Advance the playhead while playing (unless the user is scrubbing it), and keep it in view.
        if (_playing && _drag != DragMode.Playhead) AdvancePlayback(io.DeltaTime);
        if (_playing && !_panning) EnsurePlayheadVisible();

        // Which clip is highlighted: the one being dragged, else the one under the cursor.
        _hoverClipId = _drag != DragMode.None
            ? _dragClipId
            : (hovered && _editKind == EditKind.None && InLanes(mouse) ? HitClip(mouse)?.clip.Id : null);

        if (ShowBackground)
            dl.AddRectFilled(_origin, _origin + _size, Col(0.11f, 0.12f, 0.13f));

        DrawLanes(dl);
        if (_drag == DragMode.Marquee) DrawMarquee(dl);
        DrawRuler(dl);
        DrawHeaders(dl);
        DrawPlayhead(dl);
        DrawSnapGuide(dl);
        DrawCorner(dl, mouse);
        DrawTransport(dl, mouse);
        DrawHelp(dl);
        DrawInspectorStrip(dl, mouse);
        if (_inspectorOpen) DrawInspector(dl);

        if (_editKind != EditKind.None)
            DrawInlineEdit();
        else
        {
            HandleShortcuts(io);
            HandleInteraction(hovered, mouse, io);
        }

        IG.End();
    }

    private void ClampScroll()
    {
        float contentH = Document.Tracks.Count * TrackHeight;
        float viewH = _size.Y - TransportHeight - RulerHeight;
        _scrollY = Math.Clamp(_scrollY, 0f, MathF.Max(0f, contentH - viewH));
        if (_scrollX < 0) _scrollX = 0;
    }

    private void DrawLanes(ImDrawListPtr dl)
    {
        dl.PushClipRect(new Vector2(_laneLeft, _lanesTop), new Vector2(LaneRight, LanesBottom), true);

        // Row backgrounds + separators.
        for (int i = 0; i < Document.Tracks.Count; i++)
        {
            float top = TrackTop(i), bottom = top + TrackHeight;
            if (bottom < _lanesTop || top > LanesBottom) continue;
            uint bg = (i % 2 == 0) ? Col(0.135f, 0.145f, 0.155f) : Col(0.115f, 0.125f, 0.135f);
            dl.AddRectFilled(new Vector2(_laneLeft, top), new Vector2(LaneRight, bottom), bg);
            dl.AddLine(new Vector2(_laneLeft, bottom), new Vector2(LaneRight, bottom), Col(0f, 0f, 0f, 0.35f));
        }

        DrawTimeGrid(dl);

        // Clips on top of the grid.
        for (int i = 0; i < Document.Tracks.Count; i++)
        {
            float top = TrackTop(i);
            if (top + TrackHeight < _lanesTop || top > LanesBottom) continue;
            foreach (var clip in Document.Tracks[i].Clips)
                DrawClip(dl, clip, top);
        }

        dl.PopClipRect();
    }

    private void DrawTimeGrid(ImDrawListPtr dl)
    {
        int step = NiceFrameStep(80f / _pxPerFrame);
        int first = (int)MathF.Floor(XToFrame(_laneLeft) / step) * step;
        if (first < 0) first = 0;
        for (int f = first; ; f += step)
        {
            float x = FrameToX(f);
            if (x > LaneRight) break;
            if (x >= _laneLeft)
                dl.AddLine(new Vector2(x, _lanesTop), new Vector2(x, LanesBottom), Col(1f, 1f, 1f, 0.05f));
        }
    }

    private void DrawClip(ImDrawListPtr dl, ClipData clip, float trackTop)
    {
        float x0 = FrameToX(clip.Start), x1 = FrameToX(ClipEnd(clip));
        if (x1 < _laneLeft || x0 > LaneRight) return;
        float y0 = trackTop + ClipPadY, y1 = trackTop + TrackHeight - ClipPadY;
        bool hov = _hoverClipId == clip.Id;
        var (cr, cg, cb) = ClipRgb(clip.Color);
        float lift = hov ? 0.10f : 0f;   // brighten a touch on hover
        uint fill = Col(MathF.Min(1f, cr + lift), MathF.Min(1f, cg + lift), MathF.Min(1f, cb + lift));
        uint border = Col(MathF.Min(1f, cr + 0.35f), MathF.Min(1f, cg + 0.35f), MathF.Min(1f, cb + 0.35f), 0.9f);
        dl.AddRectFilled(new Vector2(x0, y0), new Vector2(x1, y1), fill, ClipRounding);

        // Fade ramps: dim the attenuated corner triangle and draw the gain line from the bottom
        // corner (level 0) up to the fade handle on the top edge (level 1). The corner-anchored
        // points are pulled onto the rounded edge (inset r - r/√2 along the diagonal) so the ramp
        // and dim triangle follow the clip's rounded corners instead of poking past them.
        var (fi, fo) = Fades(clip);
        uint dim = Col(0f, 0f, 0f, 0.42f), ramp = Col(1f, 1f, 1f, 0.9f);
        float ci = ClipRounding * 0.2929f;   // corner point offset onto the rounded edge
        if (fi > 0)
        {
            float fx = FrameToX(clip.Start + fi);
            dl.AddTriangleFilled(new Vector2(x0 + ci, y0 + ci), new Vector2(fx, y0), new Vector2(x0 + ci, y1 - ci), dim);
            dl.AddLine(new Vector2(x0 + ci, y1 - ci), new Vector2(fx, y0), ramp, 1.5f);
        }
        if (fo > 0)
        {
            float fx = FrameToX(ClipEnd(clip) - fo);
            dl.AddTriangleFilled(new Vector2(fx, y0), new Vector2(x1 - ci, y0 + ci), new Vector2(x1 - ci, y1 - ci), dim);
            dl.AddLine(new Vector2(fx, y0), new Vector2(x1 - ci, y1 - ci), ramp, 1.5f);
        }

        dl.AddRect(new Vector2(x0, y0), new Vector2(x1, y1), border, ClipRounding, ImDrawFlags.None, 1.5f);
        if (IsSelected(clip.Id))
        {
            // The primary (inspector target) gets a bolder outline; other selected clips a lighter one.
            bool primary = _selectedClipId == clip.Id;
            dl.AddRect(new Vector2(x0, y0), new Vector2(x1, y1), Col(1f, 0.93f, 0.5f, primary ? 0.95f : 0.8f),
                       ClipRounding, ImDrawFlags.None, primary ? 2.5f : 1.75f);
        }

        string lbl = string.IsNullOrWhiteSpace(clip.Label) ? clip.Id : clip.Label;
        dl.PushClipRect(new Vector2(x0 + 2, y0), new Vector2(x1 - 2, y1), true);
        var ts = IG.CalcTextSize(lbl);
        dl.AddText(new Vector2(x0 + 6, (y0 + y1) * 0.5f - ts.Y * 0.5f), Col(0.96f, 0.99f, 1f), lbl);
        dl.PopClipRect();

        // Fade handles on the top corners (brighter when the clip is hovered).
        uint hfill = hov ? Col(1f, 1f, 1f, 0.95f) : Col(1f, 1f, 1f, 0.55f);
        DrawFadeHandle(dl, clip, true, x0, x1, y0, hfill);
        DrawFadeHandle(dl, clip, false, x0, x1, y0, hfill);
    }

    private void DrawFadeHandle(ImDrawListPtr dl, ClipData clip, bool fadeIn, float x0, float x1, float y0, uint fillCol)
    {
        var (hmin, hmax) = FadeHandleRect(clip, fadeIn, x0, x1, y0);
        dl.AddRectFilled(hmin, hmax, fillCol, 2f);
        dl.AddRect(hmin, hmax, Col(0f, 0f, 0f, 0.55f), 2f);
    }

    // Effective (clamped) fade lengths, robust to out-of-range persisted values.
    private static (int fadeIn, int fadeOut) Fades(ClipData c)
    {
        int fi = Math.Clamp(c.FadeIn, 0, c.Length);
        int fo = Math.Clamp(c.FadeOut, 0, c.Length - fi);
        return (fi, fo);
    }

    // Screen rect of a clip's fade handle: a small square sitting on the top edge, centred on the
    // fade end (the clip corner at zero fade), clamped to stay within the clip's width.
    private (Vector2 min, Vector2 max) FadeHandleRect(ClipData clip, bool fadeIn, float x0, float x1, float y0)
    {
        var (fi, fo) = Fades(clip);
        float hx = fadeIn ? FrameToX(clip.Start + fi) : FrameToX(ClipEnd(clip) - fo);
        float half = FadeHandlePx * 0.5f;
        float loX = x0 + half, hiX = MathF.Max(loX, x1 - half);
        float cx = Math.Clamp(hx, loX, hiX);
        float cy = y0 + half + 1f;
        return (new Vector2(cx - half, cy - half), new Vector2(cx + half, cy + half));
    }

    private void DrawRuler(ImDrawListPtr dl)
    {
        dl.PushClipRect(new Vector2(_laneLeft, _rulerTop), new Vector2(LaneRight, _lanesTop), true);
        dl.AddRectFilled(new Vector2(_laneLeft, _rulerTop), new Vector2(LaneRight, _lanesTop), Col(0.14f, 0.15f, 0.17f));

        int step = NiceFrameStep(80f / _pxPerFrame);
        int first = (int)MathF.Floor(XToFrame(_laneLeft) / step) * step;
        if (first < 0) first = 0;
        for (int f = first; ; f += step)
        {
            float x = FrameToX(f);
            if (x > LaneRight) break;
            if (x < _laneLeft) continue;
            dl.AddLine(new Vector2(x, _lanesTop - 8), new Vector2(x, _lanesTop), Col(0.5f, 0.55f, 0.6f));
            // Vertically center the label within the ruler strip so it isn't clipped at the bottom.
            var lbl = _showTimecode ? FramesToTimecode(f, step) : f.ToString(CultureInfo.InvariantCulture);
            float ty = _rulerTop + (RulerHeight - IG.CalcTextSize(lbl).Y) * 0.5f;
            dl.AddText(new Vector2(x + 4, ty), Col(0.7f, 0.75f, 0.8f), lbl);
        }
        dl.AddLine(new Vector2(_laneLeft, _lanesTop), new Vector2(LaneRight, _lanesTop), Col(0f, 0f, 0f, 0.5f));
        dl.PopClipRect();
    }

    private void DrawPlayhead(ImDrawListPtr dl)
    {
        float x = FrameToX(Document.Playhead);
        if (x < _laneLeft || x > LaneRight) return;
        // Clip to the ruler + lanes (never over the header column or the transport bar).
        dl.PushClipRect(new Vector2(_laneLeft, _rulerTop), new Vector2(LaneRight, LanesBottom), true);
        // Snapped (Shift-scrub) turns the playhead cyan and a touch thicker as feedback.
        bool snapped = _drag == DragMode.Playhead && _playheadSnapped;
        uint col = snapped ? Col(0.30f, 0.85f, 0.98f) : Col(0.95f, 0.32f, 0.30f);
        dl.AddLine(new Vector2(x, _rulerTop + 12), new Vector2(x, LanesBottom), col, snapped ? 2.5f : 1.5f);
        // Downward-pointing handle sitting in the ruler.
        const float hw = 7f, top = 1f;
        dl.AddTriangleFilled(
            new Vector2(x - hw, _rulerTop + top),
            new Vector2(x + hw, _rulerTop + top),
            new Vector2(x, _rulerTop + top + 13f), col);
        dl.PopClipRect();
    }

    // Cyan vertical guide at the frame a clip drag is currently snapped to (After Effects style).
    private void DrawSnapGuide(ImDrawListPtr dl)
    {
        if (!_clipSnapActive) return;
        float x = FrameToX(_clipSnapFrame);
        if (x < _laneLeft || x > LaneRight) return;
        dl.PushClipRect(new Vector2(_laneLeft, _lanesTop), new Vector2(LaneRight, LanesBottom), true);
        dl.AddLine(new Vector2(x, _lanesTop), new Vector2(x, LanesBottom), Col(0.30f, 0.85f, 0.98f, 0.9f), 1.5f);
        dl.PopClipRect();
    }

    private void DrawHeaders(ImDrawListPtr dl)
    {
        dl.PushClipRect(new Vector2(_origin.X, _lanesTop), new Vector2(_laneLeft, LanesBottom), true);
        dl.AddRectFilled(new Vector2(_origin.X, _lanesTop), new Vector2(_laneLeft, LanesBottom), Col(0.15f, 0.16f, 0.18f));

        for (int i = 0; i < Document.Tracks.Count; i++)
        {
            float top = TrackTop(i), bottom = top + TrackHeight;
            if (bottom < _lanesTop || top > LanesBottom) continue;
            var tr = Document.Tracks[i];
            uint bg = (i % 2 == 0) ? Col(0.17f, 0.18f, 0.20f) : Col(0.15f, 0.16f, 0.18f);
            dl.AddRectFilled(new Vector2(_origin.X, top), new Vector2(_laneLeft, bottom), bg);
            dl.AddLine(new Vector2(_origin.X, bottom), new Vector2(_laneLeft, bottom), Col(0f, 0f, 0f, 0.35f));
            // accent tab
            dl.AddRectFilled(new Vector2(_origin.X, top), new Vector2(_origin.X + 5, bottom), Col(0.25f, 0.55f, 0.85f));
            string name = string.IsNullOrWhiteSpace(tr.Name) ? tr.Id : tr.Name;
            var ts = IG.CalcTextSize(name);
            dl.AddText(new Vector2(_origin.X + 14, top + TrackHeight * 0.5f - ts.Y * 0.5f), Col(0.9f, 0.94f, 1f), name);
        }
        // column divider
        dl.AddLine(new Vector2(_laneLeft, _lanesTop), new Vector2(_laneLeft, LanesBottom), Col(0f, 0f, 0f, 0.5f));
        dl.PopClipRect();
    }

    private void DrawCorner(ImDrawListPtr dl, Vector2 mouse)
    {
        dl.AddRectFilled(new Vector2(_origin.X, _rulerTop), new Vector2(_laneLeft, _lanesTop), Col(0.13f, 0.14f, 0.16f));
        var (bmin, bmax) = AddTrackButtonRect();
        bool hov = InRect(mouse, bmin, bmax);
        dl.AddRectFilled(bmin, bmax, hov ? Col(0.28f, 0.5f, 0.75f) : Col(0.22f, 0.4f, 0.6f), 4f);
        const string t = "+ Track";
        var ts = IG.CalcTextSize(t);
        dl.AddText(new Vector2((bmin.X + bmax.X) * 0.5f - ts.X * 0.5f, (bmin.Y + bmax.Y) * 0.5f - ts.Y * 0.5f),
                   Col(0.95f, 0.98f, 1f), t);
    }

    private void DrawHelp(ImDrawListPtr dl)
    {
        const string help =
            "dbl-click header: add/rename track   dbl-click lane: add clip   drag clip: move, edges: resize   " +
            "dbl-click clip: rename   drag empty: box-select   Shift/Ctrl+click: add to selection   " +
            "Alt/Middle: delete   Del: delete selected   drag ruler: playhead   wheel: zoom   right-drag: pan   " +
            "Space: play/stop   Shift: snap   Ctrl+Z/Y: undo/redo   Ctrl+C/V: copy/paste";
        var ts = IG.CalcTextSize(help);
        dl.AddText(new Vector2(_laneLeft + 8, LanesBottom - ts.Y - 6), Col(0.5f, 0.55f, 0.6f), help);
    }

    private (Vector2 min, Vector2 max) AddTrackButtonRect()
        => (new Vector2(_origin.X + 8, _rulerTop + 4), new Vector2(_laneLeft - 8, _lanesTop - 4));

    // ---- inspector panel ----

    // The full-height open/close strip at the far right; a chevron shows the direction.
    private void DrawInspectorStrip(ImDrawListPtr dl, Vector2 mouse)
    {
        float right = _origin.X + _size.X;
        bool hov = mouse.X >= _stripLeft && mouse.X <= right && mouse.Y >= _origin.Y && mouse.Y <= LanesBottom;
        dl.AddRectFilled(new Vector2(_stripLeft, _origin.Y), new Vector2(right, LanesBottom),
                         hov ? Col(0.20f, 0.22f, 0.25f) : Col(0.16f, 0.17f, 0.19f));
        dl.AddLine(new Vector2(_stripLeft, _origin.Y), new Vector2(_stripLeft, LanesBottom), Col(0f, 0f, 0f, 0.5f));

        float cx = (_stripLeft + right) * 0.5f, cy = (_origin.Y + LanesBottom) * 0.5f, h = 5f;
        uint c = Col(0.8f, 0.85f, 0.9f);
        if (_inspectorOpen)   // "›" — collapse
        {
            dl.AddLine(new Vector2(cx - h, cy - h), new Vector2(cx + h, cy), c, 2f);
            dl.AddLine(new Vector2(cx + h, cy), new Vector2(cx - h, cy + h), c, 2f);
        }
        else                  // "‹" — expand
        {
            dl.AddLine(new Vector2(cx + h, cy - h), new Vector2(cx - h, cy), c, 2f);
            dl.AddLine(new Vector2(cx - h, cy), new Vector2(cx + h, cy + h), c, 2f);
        }
    }

    // The clip inspector as an ImGui child window filling the panel column, hosting real widgets.
    private void DrawInspector(ImDrawListPtr dl)
    {
        float panelLeft = _canvasRight, panelRight = _stripLeft;
        float w = panelRight - panelLeft;
        if (w < 40f) return;
        dl.AddRectFilled(new Vector2(panelLeft, _origin.Y), new Vector2(panelRight, LanesBottom), Col(0.13f, 0.14f, 0.16f));

        IG.SetCursorScreenPos(new Vector2(panelLeft, _origin.Y));
        // A borderless child ignores WindowPadding unless AlwaysUseWindowPadding is set.
        IG.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(22f, 22f));
        IG.PushStyleColor(ImGuiCol.FrameBg, Col(0.17f, 0.18f, 0.21f));
        if (IG.BeginChild("interpole-inspector", new Vector2(w, _size.Y), ImGuiChildFlags.AlwaysUseWindowPadding))
            InspectorContents();
        IG.EndChild();
        IG.PopStyleColor();
        IG.PopStyleVar();
    }

    private void InspectorContents()
    {
        IG.TextDisabled("INSPECTOR");
        IG.Separator();

        if (_selection.Count > 1) { IG.Spacing(); IG.TextDisabled(_selection.Count + " clips selected — editing primary"); IG.Spacing(); }

        var found = FindClip(_selectedClipId);
        if (found is null) { IG.Spacing(); IG.TextDisabled("No clip selected.\nClick a clip, or drag a box to select several."); return; }
        var (tr, clip) = found.Value;

        // Name — live preview into the clip, one undo step on commit. Re-seed the buffer from the
        // clip whenever the field isn't being typed in, so external changes (inline rename, undo)
        // show up here too.
        IG.Text("Name");
        IG.SetNextItemWidth(-1f);
        if (!_inspNameActive || _inspNameId != clip.Id) { _inspName = clip.Label ?? ""; _inspNameId = clip.Id; }
        if (IG.InputText("##inspName", ref _inspName, 128)) clip.Label = _inspName.Trim();
        _inspNameActive = IG.IsItemActive();
        if (IG.IsItemDeactivatedAfterEdit()) Commit();

        IG.Spacing();
        IG.Text("Color");
        for (int i = 0; i < ClipPalette.Length; i++)
        {
            if (i % 5 != 0) IG.SameLine();
            var (r, g, b) = ClipPalette[i];
            if (IG.ColorButton($"##inspCol{i}", new Vector4(r, g, b, 1f),
                    ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new Vector2(30f, 24f)))
            { clip.Color = i; Commit(); }
            if (clip.Color == i)
                IG.GetWindowDrawList().AddRect(IG.GetItemRectMin(), IG.GetItemRectMax(),
                    Col(1f, 0.93f, 0.5f, 1f), 2f, ImDrawFlags.None, 2.5f);
        }

        IG.Spacing();
        IG.Separator();
        bool timeMode = _showTimecode;   // fields follow the ruler's frames/timecode mode
        IG.TextDisabled(timeMode ? "seconds (at " + Fps + " fps)" : "frames");

        var st = NumberField("Start", "start", clip.Start, timeMode, 0);
        if (st.changed) ApplyStart(tr, clip, st.value);
        if (st.done) Commit();

        var ln = NumberField("Length", "len", clip.Length, timeMode, 1);
        if (ln.changed) ApplyLength(tr, clip, ln.value);
        if (ln.done) Commit();

        var fi = NumberField("Fade In", "fin", clip.FadeIn, timeMode, 0);
        if (fi.changed) { clip.FadeIn = fi.value; ClampFades(clip); }
        if (fi.done) Commit();

        var fo = NumberField("Fade Out", "fout", clip.FadeOut, timeMode, 0);
        if (fo.changed) { clip.FadeOut = fo.value; ClampFades(clip); }
        if (fo.done) Commit();

        IG.Spacing();
        IG.TextDisabled("track: " + (string.IsNullOrWhiteSpace(tr.Name) ? tr.Id : tr.Name));
    }

    // A labelled numeric field shown in frames or seconds (per the ruler mode). Returns the new
    // frame value, whether it changed this frame, and whether the edit just finished (for Commit).
    private (int value, bool changed, bool done) NumberField(string label, string id, int frames, bool timeMode, int minFrames)
    {
        IG.Text(label);
        IG.SetNextItemWidth(-1f);
        int outFrames = frames;
        bool changed;
        if (timeMode)
        {
            float sec = frames / (float)Fps;
            changed = IG.DragFloat("##insp" + id, ref sec, 0.02f, 0f, 0f, "%.2f s");
            if (changed) outFrames = Math.Max(minFrames, (int)MathF.Round(sec * Fps));
        }
        else
        {
            int fr = frames;
            changed = IG.DragInt("##insp" + id, ref fr, 0.5f, 0, 0);
            if (changed) outFrames = Math.Max(minFrames, fr);
        }
        return (outFrames, changed, IG.IsItemDeactivatedAfterEdit());
    }

    private static void ApplyStart(TrackData tr, ClipData clip, int start)
    {
        int lo = LeftBound(tr, clip), hi = RightBound(tr, clip);
        int hiMax = hi == int.MaxValue ? int.MaxValue : hi - clip.Length;
        start = Math.Max(lo, start);
        if (hiMax != int.MaxValue) start = Math.Min(start, Math.Max(lo, hiMax));
        clip.Start = start;
    }

    private static void ApplyLength(TrackData tr, ClipData clip, int len)
    {
        int hi = RightBound(tr, clip);
        len = Math.Max(1, len);
        if (hi != int.MaxValue) len = Math.Min(len, Math.Max(1, hi - clip.Start));
        clip.Length = len;
        ClampFades(clip);
    }

    // ---- transport bar ----

    private (Vector2 min, Vector2 max) TransportButtonRect(int i)
    {
        const float pad = 8f, gap = 8f, maxBtn = 44f;
        // Square buttons: bar height minus padding, but capped so they don't grow with a tall bar.
        float bh = MathF.Min(TransportHeight - 2 * pad, maxBtn);
        float by0 = _origin.Y + (TransportHeight - bh) * 0.5f;   // vertically centered in the bar
        float by1 = by0 + bh;
        // Center the four buttons as a group across the full bar width.
        float groupW = 4 * bh + 3 * gap;
        float groupLeft = _origin.X + (_size.X - groupW) * 0.5f;
        float x0 = groupLeft + i * (bh + gap);
        return (new Vector2(x0, by0), new Vector2(x0 + bh, by1));
    }

    // The time/frames toggle (stopwatch icon) is a square button right-aligned to the far end of
    // the transport bar, matching the transport buttons' size and vertical extent.
    private (Vector2 min, Vector2 max) TimeToggleRect()
    {
        const float pad = 8f;
        var (bmin, bmax) = TransportButtonRect(3);
        float h = bmax.Y - bmin.Y;
        float x1 = LaneRight - pad;
        return (new Vector2(x1 - h, bmin.Y), new Vector2(x1, bmax.Y));
    }

    private void DrawTransport(ImDrawListPtr dl, Vector2 mouse)
    {
        dl.AddRectFilled(_origin, new Vector2(LaneRight, _rulerTop), Col(0.16f, 0.17f, 0.19f));
        dl.AddLine(new Vector2(_origin.X, _rulerTop), new Vector2(LaneRight, _rulerTop), Col(0f, 0f, 0f, 0.5f));

        // Playhead readout on the left — just the value: timecode in time mode, else a
        // zero-padded 6-digit frame counter (e.g. 000042).
        string txt = _showTimecode
            ? FramesToTimecode(Document.Playhead, 1)   // step 1 => always show the frame subfield
            : Document.Playhead.ToString("000000", CultureInfo.InvariantCulture);
        var font = IG.GetFont();
        float sz = IG.GetFontSize() * ReadoutTextScale;
        var ts = font.CalcTextSizeA(sz, float.MaxValue, 0f, txt);
        dl.AddText(font, sz, new Vector2(_origin.X + ReadoutPadX, _origin.Y + (TransportHeight - ts.Y) * 0.5f), Col(0.85f, 0.9f, 0.95f), txt);

        for (int i = 0; i < 4; i++)
        {
            var (bmin, bmax) = TransportButtonRect(i);
            bool hov = InRect(mouse, bmin, bmax);
            bool active = i == (int)TButton.Loop && _loop;
            uint bg = active ? Col(0.20f, 0.55f, 0.32f) : hov ? Col(0.28f, 0.30f, 0.34f) : Col(0.22f, 0.24f, 0.27f);
            dl.AddRectFilled(bmin, bmax, bg, 4f);
            dl.AddRect(bmin, bmax, Col(0f, 0f, 0f, 0.4f), 4f);
            DrawTransportIcon(dl, i, bmin, bmax);
        }

        // time/frames ruler-display toggle — a stopwatch icon, right-aligned. Tinted green when
        // timecode mode is on (same active style as the Loop button).
        var (rmin, rmax) = TimeToggleRect();
        bool rhov = InRect(mouse, rmin, rmax);
        uint rbg = _showTimecode ? Col(0.20f, 0.55f, 0.32f) : rhov ? Col(0.28f, 0.30f, 0.34f) : Col(0.22f, 0.24f, 0.27f);
        dl.AddRectFilled(rmin, rmax, rbg, 4f);
        dl.AddRect(rmin, rmax, Col(0f, 0f, 0f, 0.4f), 4f);
        DrawStopwatchIcon(dl, rmin, rmax, Col(0.95f, 0.97f, 1f));
    }

    // A small stopwatch glyph (ring body + top crown/stem + a hand at ~1-2 o'clock), drawn from
    // DrawList primitives to match the transport icons.
    private static void DrawStopwatchIcon(ImDrawListPtr dl, Vector2 bmin, Vector2 bmax, uint col)
    {
        float s = MathF.Min(bmax.X - bmin.X, bmax.Y - bmin.Y);
        var mid = (bmin + bmax) * 0.5f;
        var body = new Vector2(mid.X, mid.Y + s * 0.06f);   // nudge down to leave room for the crown
        float r = s * 0.30f;

        // Crown: a short stem plus a knob on top of the body.
        dl.AddLine(new Vector2(body.X, body.Y - r), new Vector2(body.X, body.Y - r - s * 0.12f), col, 2f);
        dl.AddCircleFilled(new Vector2(body.X, body.Y - r - s * 0.15f), s * 0.055f, col);

        // Body ring.
        dl.AddCircle(body, r, col, 24, 2f);

        // Hand pointing up-right (~1-2 o'clock) + a centre pivot dot.
        float a = -MathF.PI * 0.25f;
        dl.AddLine(body, new Vector2(body.X + MathF.Cos(a) * r * 0.72f, body.Y + MathF.Sin(a) * r * 0.72f), col, 2f);
        dl.AddCircleFilled(body, s * 0.035f, col);
    }

    private void DrawTransportIcon(ImDrawListPtr dl, int i, Vector2 bmin, Vector2 bmax)
    {
        var c = (bmin + bmax) * 0.5f;
        float s = MathF.Min(bmax.X - bmin.X, bmax.Y - bmin.Y) * 0.26f;
        uint col = Col(0.95f, 0.97f, 1f);
        switch ((TButton)i)
        {
            case TButton.Play: // right-pointing triangle
                dl.AddTriangleFilled(new Vector2(c.X - s, c.Y - s), new Vector2(c.X - s, c.Y + s), new Vector2(c.X + s, c.Y), col);
                break;
            case TButton.Stop: // square
                dl.AddRectFilled(new Vector2(c.X - s, c.Y - s), new Vector2(c.X + s, c.Y + s), col, 1f);
                break;
            case TButton.Rewind: // bar + left-pointing triangle  (|◄)
                dl.AddRectFilled(new Vector2(c.X - s - 3f, c.Y - s), new Vector2(c.X - s, c.Y + s), col);
                dl.AddTriangleFilled(new Vector2(c.X + s, c.Y - s), new Vector2(c.X + s, c.Y + s), new Vector2(c.X - s + 1f, c.Y), col);
                break;
            case TButton.Loop: // ring + arrowhead, tinted when active
                uint lc = _loop ? Col(0.85f, 1f, 0.9f) : col;
                dl.AddCircle(c, s * 1.15f, lc, 20, 2f);
                dl.AddTriangleFilled(
                    new Vector2(c.X + s * 1.15f, c.Y - s * 0.55f),
                    new Vector2(c.X + s * 1.15f + 6f, c.Y - s * 0.55f),
                    new Vector2(c.X + s * 1.15f + 3f, c.Y - s * 0.55f - 6f), lc);
                break;
        }
    }

    // ============================== interaction ==============================

    private bool InLanes(Vector2 m) => m.X >= _laneLeft && m.X < LaneRight && m.Y >= _lanesTop;
    private static bool InRect(Vector2 p, Vector2 min, Vector2 max)
        => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;

    // Ctrl+Z undo, Ctrl+Y / Ctrl+Shift+Z redo, Ctrl+C copy clip, Ctrl+V paste at playhead. Only
    // when no inline edit is open (there the InputText owns the keyboard).
    private void HandleShortcuts(ImGuiIOPtr io)
    {
        if (io.WantTextInput) return;   // an ImGui field (e.g. an inspector input) owns the keyboard

        // Space toggles play / stop.
        if (IG.IsKeyPressed(ImGuiKey.Space, false)) OnTransport(_playing ? TButton.Stop : TButton.Play);
        // Delete / Backspace remove the whole selection.
        if (IG.IsKeyPressed(ImGuiKey.Delete, false) || IG.IsKeyPressed(ImGuiKey.Backspace, false)) DeleteSelection();

        if (!io.KeyCtrl) return;
        if (IG.IsKeyPressed(ImGuiKey.Z, false)) { if (io.KeyShift) Redo(); else Undo(); }
        else if (IG.IsKeyPressed(ImGuiKey.Y, false)) Redo();
        else if (IG.IsKeyPressed(ImGuiKey.C, false)) CopySelection();
        else if (IG.IsKeyPressed(ImGuiKey.V, false)) PasteClipboard();
    }

    private void HandleInteraction(bool hovered, Vector2 mouse, ImGuiIOPtr io)
    {
        // Continue an in-progress drag / pan first.
        if (_drag == DragMode.Playhead) { ContinuePlayheadDrag(mouse); return; }
        if (_drag == DragMode.Marquee) { ContinueMarquee(mouse); return; }
        if (_drag != DragMode.None) { ContinueClipDrag(mouse); return; }
        if (_panning)
        {
            if (IG.IsMouseDown(ImGuiMouseButton.Right))
            {
                _scrollX -= io.MouseDelta.X;
                _scrollY -= io.MouseDelta.Y;
                if (_scrollX < 0) _scrollX = 0;
            }
            else _panning = false;
            return;
        }

        if (!hovered) return;

        // Right of the timeline: the far-right strip toggles the inspector; the panel itself is
        // driven by its own ImGui widgets. Claim the whole region so no timeline handler fires here.
        if (mouse.X >= _canvasRight)
        {
            if (mouse.X >= _stripLeft && IG.IsMouseClicked(ImGuiMouseButton.Left)) _inspectorOpen = !_inspectorOpen;
            return;
        }

        // Zoom the time axis around the cursor (only over the lanes / ruler).
        if (io.MouseWheel != 0f && mouse.X >= _laneLeft)
        {
            float old = _pxPerFrame;
            _pxPerFrame = Math.Clamp(_pxPerFrame * MathF.Pow(1.1f, io.MouseWheel), MinPxPerFrame, MaxPxPerFrame);
            float frameUnder = (mouse.X - _laneLeft + _scrollX) / old;
            _scrollX = frameUnder * _pxPerFrame - (mouse.X - _laneLeft);
            if (_scrollX < 0) _scrollX = 0;
        }

        // Right-drag pans.
        if (IG.IsMouseClicked(ImGuiMouseButton.Right)) { _panning = true; return; }

        // Transport bar (topmost strip): click a button or the time/frames toggle.
        if (mouse.Y >= _origin.Y && mouse.Y < _rulerTop)
        {
            if (IG.IsMouseClicked(ImGuiMouseButton.Left))
            {
                var (rmin, rmax) = TimeToggleRect();
                if (InRect(mouse, rmin, rmax)) _showTimecode = !_showTimecode;
                else
                    for (int i = 0; i < 4; i++)
                    {
                        var (bmin, bmax) = TransportButtonRect(i);
                        if (InRect(mouse, bmin, bmax)) { OnTransport((TButton)i); break; }
                    }
            }
            return;
        }

        // Ruler strip: click / drag to scrub the playhead.
        if (mouse.X >= _laneLeft && mouse.Y >= _rulerTop && mouse.Y < _lanesTop)
        {
            if (IG.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _drag = DragMode.Playhead;
                Document.Playhead = PlayheadFrameFromMouse(mouse.X, io.KeyShift, out _playheadSnapped);
            }
            return;
        }

        bool alt = io.KeyAlt;

        // Corner "+ Track" button.
        if (InRect(mouse, AddTrackButtonRect().min, AddTrackButtonRect().max))
        {
            if (IG.IsMouseClicked(ImGuiMouseButton.Left)) AddTrack();
            return;
        }

        // Track-header column.
        if (mouse.X < _laneLeft && mouse.Y >= _lanesTop)
        {
            var tr = TrackAtY(mouse.Y);
            if (tr is not null)
            {
                if (IG.IsMouseDoubleClicked(ImGuiMouseButton.Left)) StartEditTrackName(tr);
                else if (IG.IsMouseClicked(ImGuiMouseButton.Middle) || (alt && IG.IsMouseClicked(ImGuiMouseButton.Left)))
                    RemoveTrack(tr.Id);
            }
            else if (IG.IsMouseDoubleClicked(ImGuiMouseButton.Left)) AddTrack();
            return;
        }

        // Lane area.
        if (InLanes(mouse))
        {
            var hit = HitClip(mouse);
            if (hit is not null)
            {
                var (tr, clip, zone) = hit.Value;
                if (IG.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { StartEditClipLabel(clip); return; }
                if (IG.IsMouseClicked(ImGuiMouseButton.Middle) || (alt && IG.IsMouseClicked(ImGuiMouseButton.Left)))
                { RemoveClip(tr, clip); return; }
                if (IG.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    // Shift/Ctrl-click toggles this clip in the selection (no drag); a plain click
                    // selects just this clip and begins a move / resize / fade drag.
                    if (io.KeyShift || io.KeyCtrl) { ToggleSelected(clip.Id); return; }
                    // Grabbing a clip that's already in a multi-selection keeps the selection so the
                    // whole group drags together; grabbing any other clip selects just it.
                    if (!_selection.Contains(clip.Id)) SelectSingle(clip.Id);
                    _dragClipId = clip.Id;
                    _drag = zone switch
                    {
                        ClipZone.ResizeL => DragMode.ResizeL,
                        ClipZone.ResizeR => DragMode.ResizeR,
                        ClipZone.FadeIn => DragMode.FadeIn,
                        ClipZone.FadeOut => DragMode.FadeOut,
                        _ => DragMode.MoveClip
                    };
                    if (_drag == DragMode.MoveClip) _dragGrabFrame = XToFrame(mouse.X) - clip.Start;
                }
            }
            else if (IG.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                var tr = TrackAtY(mouse.Y);
                if (tr is not null) AddClip(tr, XToFrame(mouse.X));
            }
            else if (IG.IsMouseClicked(ImGuiMouseButton.Left))
            {
                // Empty lane → start a marquee. Shift/Ctrl keeps the current selection to add to;
                // otherwise it replaces it. A click with no drag bands nothing, so it deselects.
                _drag = DragMode.Marquee;
                _marqueeAnchor = mouse;
                _marqueeBase = (io.KeyShift || io.KeyCtrl) ? new HashSet<string>(_selection) : new HashSet<string>();
                UpdateMarqueeSelection(mouse);
            }
        }
    }

    // ---- selection ----

    private bool IsSelected(string id) => _selection.Contains(id);

    // Replace the selection with a single clip (or nothing), and make it the primary.
    private void SelectSingle(string? id)
    {
        _selection.Clear();
        _selectedClipId = id;
        if (id is not null) _selection.Add(id);
    }

    // Add a clip to the selection if absent, else remove it; keep the primary pointing at a
    // still-selected clip.
    private void ToggleSelected(string id)
    {
        if (_selection.Add(id)) _selectedClipId = id;
        else { _selection.Remove(id); if (_selectedClipId == id) _selectedClipId = _selection.FirstOrDefault(); }
    }

    // ---- marquee ----

    private void ContinueMarquee(Vector2 mouse)
    {
        if (!IG.IsMouseDown(ImGuiMouseButton.Left)) { _drag = DragMode.None; return; }
        UpdateMarqueeSelection(mouse);
    }

    // Selection = the additive base plus every clip whose on-screen rect overlaps the band.
    private void UpdateMarqueeSelection(Vector2 mouse)
    {
        var (min, max) = MarqueeRect(mouse);
        _selection.Clear();
        _selection.UnionWith(_marqueeBase);
        for (int i = 0; i < Document.Tracks.Count; i++)
        {
            float top = TrackTop(i) + ClipPadY, bottom = TrackTop(i) + TrackHeight - ClipPadY;
            if (bottom < min.Y || top > max.Y) continue;
            foreach (var clip in Document.Tracks[i].Clips)
            {
                float x0 = FrameToX(clip.Start), x1 = FrameToX(ClipEnd(clip));
                if (x1 >= min.X && x0 <= max.X) _selection.Add(clip.Id);   // rects overlap on X and Y
            }
        }
        if (_selectedClipId is null || !_selection.Contains(_selectedClipId))
            _selectedClipId = _selection.FirstOrDefault();
    }

    // The band as a normalized screen rect, its moving corner clamped to the lane area so it never
    // spills into the headers / ruler.
    private (Vector2 min, Vector2 max) MarqueeRect(Vector2 mouse)
    {
        float mx = Math.Clamp(mouse.X, _laneLeft, LaneRight);
        float my = Math.Clamp(mouse.Y, _lanesTop, LanesBottom);
        return (new Vector2(MathF.Min(_marqueeAnchor.X, mx), MathF.Min(_marqueeAnchor.Y, my)),
                new Vector2(MathF.Max(_marqueeAnchor.X, mx), MathF.Max(_marqueeAnchor.Y, my)));
    }

    private void DrawMarquee(ImDrawListPtr dl)
    {
        dl.PushClipRect(new Vector2(_laneLeft, _lanesTop), new Vector2(LaneRight, LanesBottom), true);
        var (min, max) = MarqueeRect(IG.GetMousePos());
        dl.AddRectFilled(min, max, Col(0.35f, 0.6f, 0.95f, 0.18f));
        dl.AddRect(min, max, Col(0.55f, 0.78f, 1f, 0.9f));
        dl.PopClipRect();
    }

    private void ContinuePlayheadDrag(Vector2 mouse)
    {
        if (!IG.IsMouseDown(ImGuiMouseButton.Left)) { _drag = DragMode.None; _playheadSnapped = false; Changed?.Invoke(); return; }
        Document.Playhead = PlayheadFrameFromMouse(mouse.X, IG.GetIO().KeyShift, out _playheadSnapped);
    }

    // Frame under the mouse for playhead scrubbing. With Shift held (After Effects style), snap to
    // the nearest clip start/end or fade-handle frame; otherwise round to the nearest whole frame.
    private int PlayheadFrameFromMouse(float mouseX, bool snap, out bool snapped)
    {
        snapped = false;
        int frame = Math.Max(0, (int)MathF.Round(XToFrame(mouseX)));
        if (!snap) return frame;
        CollectClipEdgeTargets(null);          // snap to every clip (nothing to exclude, no playhead)
        var r = Snap(frame);
        snapped = r.snapped;
        return r.frame;
    }

    // Fill _snapTargets with the snap-worthy frames: frame 0 and every clip's start / end / fade-in
    // end / fade-out start (skipping `exclude`, the clip being dragged so it can't snap to itself).
    private void CollectClipEdgeTargets(ClipData? exclude)
    {
        _snapTargets.Clear();
        _snapTargets.Add(0);
        foreach (var tr in Document.Tracks)
            foreach (var c in tr.Clips)
            {
                if (ReferenceEquals(c, exclude)) continue;
                var (fi, fo) = Fades(c);
                _snapTargets.Add(c.Start);
                _snapTargets.Add(ClipEnd(c));
                if (fi > 0) _snapTargets.Add(c.Start + fi);
                if (fo > 0) _snapTargets.Add(ClipEnd(c) - fo);
            }
    }

    // Nearest snap target to `frame` within SnapPx (pixel-based). Returns the snapped frame, the
    // pixel distance, and whether anything was in range. Reads the pre-filled _snapTargets.
    private (int frame, float dxPx, bool snapped) Snap(int frame)
    {
        float fx = FrameToX(frame);
        float bestDx = SnapPx;
        int best = frame;
        bool s = false;
        foreach (int t in _snapTargets)
        {
            if (t < 0) continue;
            float dx = MathF.Abs(fx - FrameToX(t));
            if (dx < bestDx) { bestDx = dx; best = t; s = true; }
        }
        return (best, bestDx, s);
    }

    private void ContinueClipDrag(Vector2 mouse)
    {
        var found = FindClip(_dragClipId);
        if (found is null || !IG.IsMouseDown(ImGuiMouseButton.Left))
        {
            _drag = DragMode.None;
            _dragClipId = null;
            _clipSnapActive = false;
            if (found is not null) Commit();   // one history entry per completed move/resize
            return;
        }

        var (tr, clip) = found.Value;
        bool shift = IG.GetIO().KeyShift;
        _clipSnapActive = false;
        // Shift snaps clip edges to other clips' edges/fades, frame 0, and the playhead.
        if (shift) { CollectClipEdgeTargets(clip); _snapTargets.Add(Document.Playhead); }
        float mf = XToFrame(mouse.X);
        switch (_drag)
        {
            case DragMode.MoveClip:
                MoveSelection(mouse, shift);   // one or many clips; also handles moving to other tracks
                break;
            case DragMode.ResizeL:
            {
                int end = ClipEnd(clip), lo = LeftBound(tr, clip);
                int raw = (int)MathF.Round(mf);
                if (shift) { var s = Snap(raw); if (s.snapped) { raw = s.frame; _clipSnapActive = true; _clipSnapFrame = s.frame; } }
                int ns = Math.Clamp(raw, lo, end - 1);
                if (_clipSnapActive && ns != _clipSnapFrame) _clipSnapActive = false;   // bound overrode the snap
                clip.Start = ns;
                clip.Length = end - ns;
                ClampFades(clip);   // shorter clip may no longer fit both fades
                break;
            }
            case DragMode.ResizeR:
            {
                int hi = RightBound(tr, clip);
                int raw = (int)MathF.Round(mf);
                if (shift) { var s = Snap(raw); if (s.snapped) { raw = s.frame; _clipSnapActive = true; _clipSnapFrame = s.frame; } }
                int ne = Math.Clamp(raw, clip.Start + 1, hi);
                if (_clipSnapActive && ne != _clipSnapFrame) _clipSnapActive = false;
                clip.Length = ne - clip.Start;
                ClampFades(clip);
                break;
            }
            case DragMode.FadeIn:
            {
                // Handle frame = Start + FadeIn; drag it between the start (no fade) and the point
                // where it would meet the fade-out.
                int f = (int)MathF.Round(mf);
                clip.FadeIn = Math.Clamp(f - clip.Start, 0, clip.Length - Math.Clamp(clip.FadeOut, 0, clip.Length));
                break;
            }
            case DragMode.FadeOut:
            {
                // Handle frame = End - FadeOut; drag it between the end (no fade) and the fade-in.
                int f = (int)MathF.Round(mf);
                clip.FadeOut = Math.Clamp(ClipEnd(clip) - f, 0, clip.Length - Math.Clamp(clip.FadeIn, 0, clip.Length));
                break;
            }
        }
    }

    // Move the selection (one clip or many) with the grabbed clip. First try to shift the whole
    // selection vertically to the track under the cursor — committed only when every selected clip
    // fits on its destination track at the cursor's time (strict no-overlap). Then apply the shared
    // horizontal delta on the (possibly new) tracks: clamped so none overlaps a non-selected
    // neighbour, with Shift snapping the grabbed clip's edges to non-selected targets.
    private void MoveSelection(Vector2 mouse, bool shift)
    {
        var grabbed = FindClip(_dragClipId);
        if (grabbed is null) return;
        var gclip = grabbed.Value.clip;

        int desiredDelta = (int)MathF.Round(XToFrame(mouse.X) - _dragGrabFrame) - gclip.Start;

        // Vertical: shift all selected clips by whole tracks toward the track under the cursor,
        // clamped so the group stays in range, and only if the whole group fits there at that time.
        int targetIdx = TrackIndexAtY(mouse.Y);
        if (targetIdx >= 0)
        {
            int trackDelta = targetIdx - Document.Tracks.IndexOf(grabbed.Value.tr);
            if (trackDelta != 0)
            {
                int minTi = int.MaxValue, maxTi = int.MinValue;
                foreach (var (tr, _) in SelectedClips())
                { int ti = Document.Tracks.IndexOf(tr); minTi = Math.Min(minTi, ti); maxTi = Math.Max(maxTi, ti); }
                trackDelta = Math.Clamp(trackDelta, -minTi, (Document.Tracks.Count - 1) - maxTi);
                if (trackDelta != 0 && GroupFits(trackDelta, desiredDelta)) ApplyTrackShift(trackDelta);
            }
        }

        // Widest delta that keeps every selected clip clear of its non-selected neighbours.
        int dMin = int.MinValue, dMax = int.MaxValue;
        foreach (var (tr, clip) in SelectedClips())
        {
            dMin = Math.Max(dMin, GroupLeftBound(tr, clip) - clip.Start);
            int hi = GroupRightBound(tr, clip);
            if (hi != int.MaxValue) dMax = Math.Min(dMax, hi - ClipEnd(clip));
        }
        if (dMin > dMax) return;   // boxed in on both sides — nothing can move

        int delta = Math.Clamp(desiredDelta, dMin, dMax);

        _clipSnapActive = false;
        if (shift)
        {
            CollectSnapTargetsExcludingSelection();
            _snapTargets.Add(Document.Playhead);
            // Consider the leading AND trailing edge of *every* selected clip (at the proposed
            // delta) and take the closest snap across the whole group — so any clip in the
            // selection can snap the move, not just the grabbed one.
            float bestDx = float.MaxValue; int snapDelta = delta, guide = 0; bool sn = false;
            foreach (var (_, clip) in SelectedClips())
            {
                var a = Snap(clip.Start + delta);                    // this clip's leading edge
                if (a.snapped && a.dxPx < bestDx) { bestDx = a.dxPx; snapDelta = a.frame - clip.Start; guide = a.frame; sn = true; }
                var b = Snap(clip.Start + clip.Length + delta);      // this clip's trailing edge
                if (b.snapped && b.dxPx < bestDx) { bestDx = b.dxPx; snapDelta = b.frame - clip.Length - clip.Start; guide = b.frame; sn = true; }
            }
            if (sn && Math.Clamp(snapDelta, dMin, dMax) == snapDelta)   // dropped if a bound overrides it
            { delta = snapDelta; _clipSnapActive = true; _clipSnapFrame = guide; }
        }

        if (delta == 0) return;
        foreach (var (_, clip) in SelectedClips()) clip.Start += delta;
    }

    private IEnumerable<(TrackData tr, ClipData clip)> SelectedClips()
    {
        foreach (var tr in Document.Tracks)
            foreach (var c in tr.Clips)
                if (_selection.Contains(c.Id)) yield return (tr, c);
    }

    // Would every selected clip, shifted by trackDelta tracks and timeDelta frames, sit on a valid
    // track (>= 0) at a valid time (>= 0) without overlapping any non-selected clip there? Other
    // selected clips don't count — they move too, and a uniform track shift keeps them from colliding.
    private bool GroupFits(int trackDelta, int timeDelta)
    {
        foreach (var (tr, clip) in SelectedClips())
        {
            int ti = Document.Tracks.IndexOf(tr) + trackDelta;
            if (ti < 0 || ti >= Document.Tracks.Count) return false;
            int s = clip.Start + timeDelta, e = s + clip.Length;
            if (s < 0) return false;
            foreach (var o in Document.Tracks[ti].Clips)
                if (!_selection.Contains(o.Id) && s < ClipEnd(o) && o.Start < e) return false;
        }
        return true;
    }

    // Move every selected clip trackDelta tracks down (negative = up). Collect the moves first, then
    // re-home the clips, so we never mutate a track's list while enumerating it.
    private void ApplyTrackShift(int trackDelta)
    {
        var moves = new List<(TrackData from, TrackData to, ClipData clip)>();
        foreach (var (tr, clip) in SelectedClips())
            moves.Add((tr, Document.Tracks[Document.Tracks.IndexOf(tr) + trackDelta], clip));
        foreach (var (from, to, clip) in moves) { from.Clips.Remove(clip); to.Clips.Add(clip); }
    }

    // Neighbour bounds like LeftBound/RightBound, but treating every selected clip as movable (so
    // the group is bounded only by the clips that stay put).
    private int GroupLeftBound(TrackData tr, ClipData clip)
    {
        int lo = 0;
        foreach (var o in tr.Clips)
            if (!_selection.Contains(o.Id) && o.Start < clip.Start) lo = Math.Max(lo, ClipEnd(o));
        return lo;
    }
    private int GroupRightBound(TrackData tr, ClipData clip)
    {
        int hi = int.MaxValue;
        foreach (var o in tr.Clips)
            if (!_selection.Contains(o.Id) && o.Start > clip.Start) hi = Math.Min(hi, o.Start);
        return hi;
    }

    // Snap targets (frame 0 + clip starts/ends/fade points) from clips outside the selection, for
    // snapping a group move against the clips that stay put.
    private void CollectSnapTargetsExcludingSelection()
    {
        _snapTargets.Clear();
        _snapTargets.Add(0);
        foreach (var tr in Document.Tracks)
            foreach (var c in tr.Clips)
            {
                if (_selection.Contains(c.Id)) continue;
                var (fi, fo) = Fades(c);
                _snapTargets.Add(c.Start);
                _snapTargets.Add(ClipEnd(c));
                if (fi > 0) _snapTargets.Add(c.Start + fi);
                if (fo > 0) _snapTargets.Add(ClipEnd(c) - fo);
            }
    }

    // Keep a clip's fades non-negative and non-overlapping within its (possibly shrunk) length.
    private static void ClampFades(ClipData c)
    {
        c.FadeIn = Math.Clamp(c.FadeIn, 0, c.Length);
        c.FadeOut = Math.Clamp(c.FadeOut, 0, c.Length - c.FadeIn);
    }

    // ---- neighbour bounds (non-overlap) ----
    // Immediate left neighbour's end (0 if none); the clip's Start may not go below this.
    private static int LeftBound(TrackData tr, ClipData clip)
    {
        int lo = 0;
        foreach (var o in tr.Clips)
            if (o != clip && o.Start < clip.Start) lo = Math.Max(lo, ClipEnd(o));
        return lo;
    }
    // Immediate right neighbour's start (int.MaxValue if none); the clip's End may not exceed it.
    private static int RightBound(TrackData tr, ClipData clip)
    {
        int hi = int.MaxValue;
        foreach (var o in tr.Clips)
            if (o != clip && o.Start > clip.Start) hi = Math.Min(hi, o.Start);
        return hi;
    }

    // ============================== hit tests ==============================

    private int TrackIndexAtY(float y)
    {
        if (y < _lanesTop) return -1;
        int i = (int)MathF.Floor((y - _lanesTop + _scrollY) / TrackHeight);
        return (i >= 0 && i < Document.Tracks.Count) ? i : -1;
    }

    private TrackData? TrackAtY(float y)
    {
        int i = TrackIndexAtY(y);
        return i < 0 ? null : Document.Tracks[i];
    }

    private enum ClipZone { Body, ResizeL, ResizeR, FadeIn, FadeOut }

    // Returns the clip under the mouse plus which grab zone. Fade handles (small squares on the top
    // corners) take priority over the resize edges they sit on; the rest of an edge stays resize.
    private (TrackData tr, ClipData clip, ClipZone zone)? HitClip(Vector2 mouse)
    {
        int i = TrackIndexAtY(mouse.Y);
        if (i < 0) return null;
        var tr = Document.Tracks[i];
        float top = TrackTop(i);
        float y0 = top + ClipPadY, y1 = top + TrackHeight - ClipPadY;
        if (mouse.Y < y0 || mouse.Y > y1) return null;

        foreach (var clip in tr.Clips)
        {
            float x0 = FrameToX(clip.Start), x1 = FrameToX(ClipEnd(clip));
            if (mouse.X < x0 || mouse.X > x1) continue;

            var (inMin, inMax) = FadeHandleRect(clip, true, x0, x1, y0);
            if (InRect(mouse, inMin, inMax)) return (tr, clip, ClipZone.FadeIn);
            var (outMin, outMax) = FadeHandleRect(clip, false, x0, x1, y0);
            if (InRect(mouse, outMin, outMax)) return (tr, clip, ClipZone.FadeOut);

            var zone = (mouse.X - x0 <= EdgeGrabPx) ? ClipZone.ResizeL
                     : (x1 - mouse.X <= EdgeGrabPx) ? ClipZone.ResizeR
                     : ClipZone.Body;
            return (tr, clip, zone);
        }
        return null;
    }

    private (TrackData tr, ClipData clip)? FindClip(string? id)
    {
        if (id is null) return null;
        foreach (var tr in Document.Tracks)
            foreach (var c in tr.Clips)
                if (c.Id == id) return (tr, c);
        return null;
    }

    // ============================== mutations ==============================

    private void AddTrack()
    {
        _trackCounter++;
        Document.Tracks.Add(new TrackData { Id = InterpoleDocument.NewId("tr"), Name = "Track " + _trackCounter });
        Commit();
    }

    private void RemoveTrack(string id)
    {
        Document.Tracks.RemoveAll(t => t.Id == id);
        _selection.RemoveWhere(cid => FindClip(cid) is null);   // drop clips that lived on this track
        if (FindClip(_selectedClipId) is null) _selectedClipId = _selection.FirstOrDefault();
        Commit();
    }

    private void AddClip(TrackData tr, float frameF)
    {
        int f = Math.Max(0, (int)MathF.Round(frameF));
        int leftEnd = 0, rightStart = int.MaxValue;
        foreach (var c in tr.Clips)
        {
            if (ClipEnd(c) <= f) leftEnd = Math.Max(leftEnd, ClipEnd(c));
            else if (c.Start >= f) rightStart = Math.Min(rightStart, c.Start);
            else return; // f is inside an existing clip — nothing to add here
        }
        int start = Math.Max(f, leftEnd);
        int maxLen = rightStart == int.MaxValue ? DefaultClipLen : rightStart - start;
        int len = Math.Min(DefaultClipLen, maxLen);
        if (len < 1) return;

        _clipCounter++;
        var nc = new ClipData { Id = InterpoleDocument.NewId("cl"), Label = "clip " + _clipCounter, Start = start, Length = len };
        tr.Clips.Add(nc);
        SelectSingle(nc.Id);
        Commit();
    }

    private void RemoveClip(TrackData tr, ClipData clip)
    {
        tr.Clips.Remove(clip);
        _selection.Remove(clip.Id);
        if (_selectedClipId == clip.Id) _selectedClipId = _selection.FirstOrDefault();
        Commit();
    }

    // Delete every selected clip in one undo step.
    private void DeleteSelection()
    {
        if (_selection.Count == 0) return;
        int removed = 0;
        foreach (var tr in Document.Tracks)
            removed += tr.Clips.RemoveAll(c => _selection.Contains(c.Id));
        _selection.Clear();
        _selectedClipId = null;
        if (removed > 0) Commit();
    }

    // ---- copy / paste ----

    private static ClipData CloneClip(ClipData c) => new()
    {
        Label = c.Label, Start = c.Start, Length = c.Length,
        Color = c.Color, FadeIn = c.FadeIn, FadeOut = c.FadeOut
    };

    // Copy every selected clip (or, if nothing is selected, the hovered one) to the clipboard,
    // recording each clip's track index and its start relative to the earliest copied clip, so a
    // paste can rebuild the whole group at the playhead.
    private void CopySelection()
    {
        _clipboard.Clear();
        var picked = new List<(ClipData clip, int trackIndex)>();
        for (int i = 0; i < Document.Tracks.Count; i++)
            foreach (var c in Document.Tracks[i].Clips)
                if (_selection.Contains(c.Id)) picked.Add((c, i));

        if (picked.Count == 0)   // fallback: the hovered clip, so bare Ctrl+C still copies one
        {
            var hov = FindClip(_hoverClipId);
            if (hov is null) return;
            picked.Add((hov.Value.clip, Document.Tracks.IndexOf(hov.Value.tr)));
        }

        int minStart = picked.Min(p => p.clip.Start);
        foreach (var (clip, trackIndex) in picked)
            _clipboard.Add(new ClipCopy(CloneClip(clip), trackIndex, clip.Start - minStart));
    }

    // Paste the clipboard at the playhead: the earliest copied clip lands on the playhead (stepped
    // past any clip covering it on its track, as the single-clip paste did), the rest keep their
    // relative track + time offsets. Each pasted clip is trimmed to the gap on its track and dropped
    // if there's no room, so a paste never overlaps. The pasted clips become the new selection.
    private void PasteClipboard()
    {
        if (_clipboard.Count == 0) return;

        var anchor = _clipboard.OrderBy(e => e.StartOffset).First();
        int baseStart = Document.Playhead;
        if (anchor.TrackIndex < Document.Tracks.Count)
            foreach (var c in Document.Tracks[anchor.TrackIndex].Clips.OrderBy(c => c.Start))
                if (c.Start <= baseStart && baseStart < ClipEnd(c)) baseStart = ClipEnd(c);

        var newSel = new List<string>();
        foreach (var e in _clipboard.OrderBy(e => e.TrackIndex).ThenBy(e => e.StartOffset))
        {
            if (e.TrackIndex >= Document.Tracks.Count) continue;   // track gone (fewer tracks now)
            var tr = Document.Tracks[e.TrackIndex];
            int start = baseStart + e.StartOffset;

            bool covered = false;
            int rightStart = int.MaxValue;
            foreach (var c in tr.Clips)
            {
                if (c.Start <= start && start < ClipEnd(c)) { covered = true; break; }
                if (c.Start > start) rightStart = Math.Min(rightStart, c.Start);
            }
            if (covered) continue;   // its slot is occupied — skip this one, keep the rest of the group

            int maxLen = rightStart == int.MaxValue ? e.Clip.Length : rightStart - start;
            int len = Math.Min(e.Clip.Length, maxLen);
            if (len < 1) continue;

            var nc = new ClipData
            {
                Id = InterpoleDocument.NewId("cl"),
                Label = e.Clip.Label, Start = start, Length = len,
                Color = e.Clip.Color, FadeIn = e.Clip.FadeIn, FadeOut = e.Clip.FadeOut
            };
            ClampFades(nc);   // a trimmed paste may no longer fit both fades
            tr.Clips.Add(nc);
            newSel.Add(nc.Id);
        }

        if (newSel.Count == 0) return;   // nothing fit → no-op, leave the current selection alone
        _selection.Clear();
        _selection.UnionWith(newSel);
        _selectedClipId = newSel[0];
        Commit();
    }

    // ============================== undo / redo ==============================

    // A structural edit just committed: record an undo step (if anything actually changed) and
    // persist. Playhead-only changes (scrub / Stop / Rewind) call Changed directly instead, so
    // they never create history entries.
    private void Commit()
    {
        PushUndo();
        Changed?.Invoke();
    }

    // Serialize just the structure (tracks), with the playhead normalized out, so history compares
    // and restores structure only — the live playhead is preserved across undo/redo.
    private string StructuralSnapshot()
        => new InterpoleDocument { Title = Document.Title, Tracks = Document.Tracks, Playhead = 0 }.Serialize();

    private void PushUndo()
    {
        string current = StructuralSnapshot();
        if (current == _baseline) return;      // no real structural change → no history entry
        _undo.Add(_baseline);
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
        _baseline = current;
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Add(_baseline);
        _baseline = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        ApplySnapshot(_baseline);
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add(_baseline);
        _baseline = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        ApplySnapshot(_baseline);
    }

    // Replace the document structure with a snapshot's, keeping the live playhead and cancelling any
    // in-progress interaction (its clip/track may no longer exist), then persist.
    private void ApplySnapshot(string json)
    {
        var snap = InterpoleDocument.Parse(json);
        Document.Title = snap.Title;
        Document.Tracks = snap.Tracks;         // playhead intentionally left as-is

        _drag = DragMode.None;
        _dragClipId = null;
        _panning = false;
        _hoverClipId = null;
        _editKind = EditKind.None;

        Changed?.Invoke();
    }

    // ============================== transport / playback ==============================

    private void OnTransport(TButton b)
    {
        switch (b)
        {
            case TButton.Play:
                // Parked at the end? Start over from the top instead of staying stuck there.
                if (Document.Playhead >= ContentEnd()) { Document.Playhead = 0; EnsurePlayheadVisible(); Changed?.Invoke(); }
                _playing = true; _playAccum = 0f;
                break;
            case TButton.Stop: _playing = false; _playAccum = 0f; Changed?.Invoke(); break;        // save where it stopped
            case TButton.Rewind: Document.Playhead = 0; _playAccum = 0f; EnsurePlayheadVisible(); Changed?.Invoke(); break; // jump to start + bring it into view

            case TButton.Loop: _loop = !_loop; break;
        }
    }

    // Advance the playhead while playing. Default: by real elapsed time (dt × Fps), accumulating
    // fractional frames so 1s = 1s no matter the render rate. Legacy FrameStepPlayback mode: one
    // document-frame per rendered frame (render-rate bound). Deliberately NOT persisted per step
    // (that would call SetPinValue every frame); the position is saved on Stop / Rewind / scrub.
    private void AdvancePlayback(float dt)
    {
        int end = ContentEnd();
        if (end <= 0) { _playing = false; _playAccum = 0f; return; }

        int advance;
        if (FrameStepPlayback)
            advance = 1;
        else
        {
            _playAccum += MathF.Max(0f, dt) * _fps;
            advance = (int)_playAccum;   // whole frames elapsed this render; keep the remainder
            _playAccum -= advance;
        }
        if (advance <= 0) return;

        long np = (long)Document.Playhead + advance;
        if (np < end) { Document.Playhead = (int)np; return; }

        if (_loop) Document.Playhead = (int)(np % end);        // wrap, carrying the overshoot
        else { Document.Playhead = end; _playing = false; _playAccum = 0f; }
    }

    // While playing, keep the playhead on-screen: if it has left the lane view, page the view so
    // it reappears just inside the left edge, leaving the rest of the width ahead to play into
    // (paging rather than edge-nudging avoids re-scrolling every frame at the right edge). Only the
    // time axis (_scrollX) moves; the track scroll is left alone.
    private void EnsurePlayheadVisible()
    {
        float x = FrameToX(Document.Playhead);
        if (x >= _laneLeft && x <= LaneRight) return;
        float margin = (LaneRight - _laneLeft) * 0.05f;
        _scrollX = Document.Playhead * _pxPerFrame - margin;
        if (_scrollX < 0) _scrollX = 0;
    }

    // The last frame covered by any clip — the natural end of the timeline (0 if empty).
    private int ContentEnd()
    {
        int end = 0;
        foreach (var tr in Document.Tracks)
            foreach (var c in tr.Clips)
                end = Math.Max(end, ClipEnd(c));
        return end;
    }

    // ============================== inline editing ==============================

    private void StartEditTrackName(TrackData tr)
    {
        _editKind = EditKind.TrackName;
        _editId = tr.Id;
        _editBuffer = tr.Name ?? "";
        _focusEdit = true;
    }

    private void StartEditClipLabel(ClipData clip)
    {
        _editKind = EditKind.ClipLabel;
        _editId = clip.Id;
        _editBuffer = clip.Label ?? "";
        _focusEdit = true;
    }

    private void DrawInlineEdit()
    {
        Vector2 pos;
        float width;
        ClipData? editClip = null;
        if (_editKind == EditKind.TrackName)
        {
            int i = Document.Tracks.FindIndex(t => t.Id == _editId);
            if (i < 0) { _editKind = EditKind.None; return; }
            float top = TrackTop(i);
            pos = new Vector2(_origin.X + 10, top + TrackHeight * 0.5f - IG.GetFrameHeight() * 0.5f);
            width = HeaderWidth - 18;
        }
        else
        {
            var found = FindClip(_editId);
            if (found is null) { _editKind = EditKind.None; return; }
            var (tr, clip) = found.Value;
            editClip = clip;
            int i = Document.Tracks.IndexOf(tr);
            float top = TrackTop(i);
            float x0 = FrameToX(clip.Start), x1 = FrameToX(ClipEnd(clip));
            pos = new Vector2(x0 + 3, top + ClipPadY + 2);
            width = MathF.Max(40f, x1 - x0 - 6f);
        }

        // Opaque field background so the canvas doesn't bleed through the edit box.
        IG.PushStyleColor(ImGuiCol.FrameBg, Col(0.16f, 0.17f, 0.19f));
        IG.PushStyleColor(ImGuiCol.FrameBgHovered, Col(0.22f, 0.23f, 0.26f));
        IG.PushStyleColor(ImGuiCol.FrameBgActive, Col(0.24f, 0.25f, 0.28f));

        IG.SetCursorScreenPos(pos);
        IG.SetNextItemWidth(width);
        if (_focusEdit) { IG.SetKeyboardFocusHere(); _focusEdit = false; }

        var flags = ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll;
        bool commit = IG.InputText("##interpole-edit", ref _editBuffer, 128, flags);
        bool lostFocus = IG.IsItemDeactivated();

        IG.PopStyleColor(3);

        // While renaming a clip, offer the colour palette below the field. A swatch click sets the
        // colour (shown live) but keeps the rename open — clicking it deactivated the field, so we
        // swallow that lost-focus and re-focus the field next frame instead of committing.
        if (editClip is not null && DrawClipColorSwatches(editClip, pos)) { _focusEdit = true; return; }

        if (commit || lostFocus) CommitEdit();
    }

    // Draw the 10-colour swatch strip just below the rename field (flipped above if it would fall
    // off the bottom) and hit-test it. Returns true if a swatch was clicked this frame, having set
    // the clip's colour. Persistence happens when the rename commits (single history entry).
    private bool DrawClipColorSwatches(ClipData clip, Vector2 editPos)
    {
        var dl = IG.GetWindowDrawList();
        Vector2 mouse = IG.GetMousePos();
        const float sw = 22f, gap = 4f, pad = 5f;
        int n = ClipPalette.Length;
        float rowW = n * sw + (n - 1) * gap;
        float panelW = rowW + pad * 2, panelH = sw + pad * 2;

        float minX = _laneLeft + 4f, maxX = LaneRight - 4f - panelW;
        float px = maxX < minX ? minX : Math.Clamp(editPos.X - pad, minX, maxX);   // avoid min>max
        float py = editPos.Y + IG.GetFrameHeight() + 6f;
        if (py + panelH > LanesBottom - 4f) py = editPos.Y - 6f - panelH;   // flip above near the bottom

        dl.AddRectFilled(new Vector2(px, py), new Vector2(px + panelW, py + panelH), Col(0.10f, 0.11f, 0.12f, 0.97f), 4f);
        dl.AddRect(new Vector2(px, py), new Vector2(px + panelW, py + panelH), Col(0f, 0f, 0f, 0.5f), 4f);

        bool clicked = false;
        for (int i = 0; i < n; i++)
        {
            float sx = px + pad + i * (sw + gap), sy = py + pad;
            var smin = new Vector2(sx, sy);
            var smax = new Vector2(sx + sw, sy + sw);
            var (r, g, b) = ClipPalette[i];
            dl.AddRectFilled(smin, smax, Col(r, g, b), 3f);
            bool hov = InRect(mouse, smin, smax);
            if (clip.Color == i)
                dl.AddRect(smin, smax, Col(1f, 1f, 1f, 0.95f), 3f, ImDrawFlags.None, 2f);
            else if (hov)
                dl.AddRect(smin, smax, Col(1f, 1f, 1f, 0.6f), 3f, ImDrawFlags.None, 1.5f);
            if (hov && IG.IsMouseClicked(ImGuiMouseButton.Left)) { clip.Color = i; clicked = true; }
        }
        return clicked;
    }

    private void CommitEdit()
    {
        if (_editKind == EditKind.TrackName)
        {
            var tr = Document.Tracks.FirstOrDefault(t => t.Id == _editId);
            if (tr is not null) tr.Name = _editBuffer.Trim();
        }
        else if (_editKind == EditKind.ClipLabel)
        {
            var found = FindClip(_editId);
            if (found is not null) found.Value.clip.Label = _editBuffer.Trim();
        }
        _editKind = EditKind.None;
        Commit();   // rename to the same value is a no-op step (PushUndo skips unchanged structure)
    }

    // Format a frame count as timecode using the display Fps. The frame subfield (M:SS:FF) is
    // shown only when the ruler step is sub-second; on whole-second steps a clean M:SS is enough.
    private string FramesToTimecode(int frame, int step)
    {
        int totalSec = frame / _fps;
        int mm = totalSec / 60, ss = totalSec % 60, ff = frame % _fps;
        return step % _fps == 0 ? $"{mm}:{ss:00}" : $"{mm}:{ss:00}:{ff:00}";
    }

    // A "nice" frame step (1,2,5,10,15,20,30,60,...) at least as large as raw, so ruler ticks
    // land on readable frame numbers regardless of zoom.
    private static int NiceFrameStep(float raw)
    {
        if (raw < 1) return 1;
        int[] steps = { 1, 2, 5, 10, 15, 20, 30, 60, 120, 150, 300, 600, 900, 1200, 1800, 3600 };
        foreach (var s in steps)
            if (s >= raw) return s;
        int big = 3600;
        while (big < raw) big *= 2;
        return big;
    }

    public void Dispose() { }
}
