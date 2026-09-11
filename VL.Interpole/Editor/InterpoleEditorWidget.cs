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
    private const float HeaderWidth = 340f;   // track-name column (timeline) — matches the clip page
    private const float ClipHeaderWidth = 340f;// wider track column on the clip (keyframe) page
    private const float TransportHeight = 88f;// transport toolbar (playhead readout + buttons)
    private const float RulerHeight = 64f;    // frame ruler strip (tall enough for the labels)
    private const float LoopStripHeight = 14f;// loop-range ribbon, carved from the top of the ruler
    private const float MarkerLaneHeight = TrackHeight;// marker strip; same height as a normal track lane
    private const float MarkerPinHeight = MarkerLaneHeight - 10f;// pin height (fills the lane bar a small margin), centred vertically
    private const float TrackHeight = 54f;    // one standard lane
    private const float AudioTrackScale = 1.2f; // audio tracks/clips are 20% taller
    private const float ClipPadY = 6f;        // vertical inset of a clip within its lane
    private const float ClipRounding = 4f;    // corner radius of a clip's rounded rect
    private const float EdgeGrabPx = 6f;      // width of a clip's resize handle
    private const float FadeHandlePx = 11f;   // size of a clip's top-corner fade handle
    private const int DefaultClipLen = 30;    // frames, for a freshly added clip
    private const float MinPxPerFrame = 0.06f, MaxPxPerFrame = 40f;   // 0.06/6 ≈ 1% zoom floor
    private const float DefaultPxPerFrame = 6f;  // the 100% zoom reference
    private const float ReadoutTextScale = 1f; // playhead/timecode readout size, x the base font
    private const float InspectorWidth = 280f;   // right-side clip inspector panel
    private const float ToggleStripPx = 18f;     // inspector open/close strip at the far right
    private const float ReadoutPadX = 24f;     // left inset of the readout from the bar edge
    private const float TabBarHeight = 52f;    // bottom page-switcher strip [Timeline] [◆ clip]

    private readonly string _title;
    private int _trackCounter;
    private int _clipCounter;

    public InterpoleDocument Document { get; }
    // When false the canvas is drawn transparent — both the explicit fill AND ImGui's own
    // window background are skipped — so whatever is behind the renderer shows through.
    public bool ShowBackground = true;

    // Playback render mode, set by the node each frame so the editor can render only what changes
    // while playing (the snapshot-cache path — see SnapshotLayer):
    //   Off     = full live editor (normal editing).
    //   Static  = everything EXCEPT the moving overlay (playhead / timecode), for capture into the cache.
    //   Overlay = ONLY the playhead + timecode readout, drawn over the blitted cache; window transparent.
    // In both play passes, editing is locked — only transport, playhead scrub and zoom are handled.
    public enum PlayCache { Off, Static, Overlay }
    public PlayCache PlayPass = PlayCache.Off;

    // Read by the node (before Update) to drive the per-page cache: the node follows the playhead in
    // whole-page steps from the lane width + zoom, sets the scroll, and keys the cache by that scroll.
    public float ViewScrollX => _scrollX;
    public float ViewPxPerFrame => _pxPerFrame;
    public Vector2 ViewSize => _size;
    public float LaneWidthPx => LaneRight - _laneLeft;   // visible time-axis width (one page), in px
    // Set the time-axis scroll while playing (the node owns the view during play, stepping it one page
    // at a time so the playhead stays on a grid whose cached pages recur exactly on loops).
    public void SetPlayScroll(float scrollX) => _scrollX = MathF.Max(0f, scrollX);

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
    private float _pxPerFrame = DefaultPxPerFrame;
    private float _scrollX;   // pixels scrolled along time, >= 0
    private float _scrollY;   // pixels scrolled through the track stack, >= 0

    // ---- per-frame cached screen geometry (set at the top of Draw) ----
    private Vector2 _origin;
    private Vector2 _size;
    private float _laneLeft;  // x where the clip lanes begin (= _origin.X + HeaderWidth)
    private float _rulerTop;    // y where the ruler begins (= _origin.Y + TransportHeight)
    private float _loopBottom;  // y where the loop strip ends inside the ruler (= _rulerTop + LoopStripHeight)
    private float _rulerBottom; // y where the ruler ends (= _rulerTop + RulerHeight); the marker lane sits in [_rulerBottom, _lanesTop)
    private float _lanesTop;    // y where the track lanes begin (= _rulerBottom + MarkerLaneHeight)

    // ---- interaction / drag state ----
    private enum DragMode { None, MoveClip, ResizeL, ResizeR, FadeIn, FadeOut, Playhead, Marquee, LoopIn, LoopOut, LoopMove, MoveMarker, MoveTrack, KfMarquee, MoveKeyframe, MoveFloatTrack }
    private DragMode _drag;
    private string? _dragClipId;
    private string? _dragMarkerId;  // MoveMarker: the marker being dragged along the frame axis
    private string? _dragTrackId;   // MoveTrack: the track header being dragged to reorder
    private float _dragGrabFrame;   // MoveClip: (mouseFrame - clip.Start) captured at grab
    private float _loopGrabFrame;   // LoopMove: (mouseFrame - LoopIn) captured at grab
    private bool _panning;
    private string? _hoverClipId;
    private ClipZone? _hoverZone;   // grab zone under the cursor on the hovered clip, for the edge cue

    // Which "page" the editor shows: the timeline, or a clip's keyframe editor. The bottom tab bar has
    // a [Timeline] tab plus one tab per opened clip (in open order). Clicking a (non-audio) clip on the
    // timeline opens a tab for it; clicking a tab shows that clip's page; a tab's ✕ closes it. Audio
    // clips have no keyframes yet, so they never get a tab. _activeClipId is the clip shown in clip mode.
    private enum EditorMode { Timeline, Clip }
    private EditorMode _mode = EditorMode.Timeline;
    private string? _activeClipId;
    private readonly List<string> _openTabs = new();   // clip ids with a bottom tab, in open order
    // Bottom-tab hit rects, rebuilt by DrawTabBar each frame (draw runs before interaction).
    private Vector2 _tlTabMin, _tlTabMax;
    private readonly List<(string id, Vector2 min, Vector2 max, Vector2 closeMin, Vector2 closeMax)> _tabHits = new();

    private string? _selectedClipId;  // primary selection: shown in the inspector; the drag target
    // The marker shown in the inspector, if any. Mutually exclusive with a clip selection: selecting a
    // marker clears the clip selection and vice-versa, so the inspector edits exactly one thing.
    private string? _selectedMarkerId;
    // Full multi-selection (marquee or Shift/Ctrl-click). Contains _selectedClipId when non-null.
    // Highlighted clips + the copy source. Ids, not references, so it survives the document rebuilds
    // that undo/redo do (clip Ids are stable across serialization).
    private readonly HashSet<string> _selection = new();
    private bool _playheadSnapped;   // set while a Shift-scrub is snapped, for playhead feedback

    // ---- marquee (rubber-band) selection ----
    private Vector2 _marqueeAnchor;               // fixed corner (screen space), set on press
    private HashSet<string> _marqueeBase = new();  // selection to union with (additive marquee)

    // ---- keyframe selection / drag (clip page) ----
    private readonly HashSet<string> _kfSelection = new();          // selected keyframe ids (active clip)
    private readonly List<(int trackIndex, int frame, float value)> _kfClipboard = new();  // copied keyframes
    private readonly List<(Keyframe kf, int orig)> _kfDragOrig = new();  // MoveKeyframe: frames captured at grab
    private int _kfDragGrabbedOrig;        // MoveKeyframe: the grabbed keyframe's frame at grab
    private float _kfDragGrabMouseFrame;   // MoveKeyframe: XToFrame(mouseX) at grab
    private int _kfDragMinOrig;            // MoveKeyframe: min original frame in the group (lower clamp)

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
    // The name of a marker the playhead crossed during the most recent advance step without landing on
    // it — a plain marker jumped over between two frames when the advance was more than one frame. Set in
    // AdvancePlayback (reset at its top), so it is valid only for the single frame it was set and only
    // while playing; the node reports it via ActiveMarkerName so a skipped marker still flags active for
    // that one frame. A marker the playhead actually lands/parks on is reported from the playhead instead.
    private string? _crossedMarkerName;
    // Wall-clock source for playback timing. Playback is advanced from the node's Update (via Tick),
    // NOT from the ImGui frame — so it keeps running in No UI Mode, where there is no frame and thus
    // no ImGui io.DeltaTime. _lastTickSec is refreshed every Tick so the delta stays a single frame.
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastTickSec;
    // Ruler + playhead readout show timecode (M:SS[:FF] via Fps) instead of raw frame numbers.
    // A view preference only — session-transient, like _playing / _loop; not saved in the document.
    // Defaults to timecode (the stopwatch toggle on).
    private bool _showTimecode = true;

    // ---- inline editing ----
    private enum EditKind { None, TrackName, ClipLabel, FloatTrackName }
    private EditKind _editKind;
    private string _editId = "";
    private string _editBuffer = "";
    private bool _focusEdit;

    // ---- marker create/edit modal ----
    // A small ImGui popup (name field + "stop playback" checkbox) shown before a marker is placed —
    // and reused to edit an existing one. _markerDialogId is null for a new marker (added on OK) or
    // the id of the marker being edited. Interaction/shortcuts are suppressed while _markerDialogOpen.
    private bool _markerDialogOpen;
    private bool _markerDialogRequestOpen;   // set for one frame to trigger IG.OpenPopup
    private bool _markerDialogFocus;          // focus the name field when the popup first appears
    private string? _markerDialogId;          // null = creating; else the marker being edited
    private int _markerDialogFrame;           // frame the new/edited marker sits on
    private string _markerNameBuffer = "";
    private bool _markerStopBuffer;
    private string _markerJumpTargetBuffer = "";  // jump-to marker Id chosen in the dialog ("" = none)
    private int _markerColorBuffer;           // palette index chosen in the dialog

    // ---- keyframe interpolation context menu (clip page) ----
    // A small right-click popup listing the six InterpMode types, applied to the keyframe selection.
    // Interaction/shortcuts are suppressed while _kfMenuOpen (like the marker dialog).
    private bool _kfMenuOpen;
    private bool _kfMenuRequestOpen;   // set for one frame to trigger IG.OpenPopup
    private Vector2 _kfMenuPos;        // screen position to open the popup at (the right-click point)

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
    // The palette + colour lookup now live in the Engine (InterpolePalette) so the editor's
    // swatches / clip fill and the node's Active Color output share one source of truth. These
    // thin aliases keep the local call sites terse and unchanged.
    private static (float r, float g, float b)[] ClipPalette => InterpolePalette.Colors;

    private static (float r, float g, float b) ClipRgb(int i) => InterpolePalette.Rgb(i);

    // ---- time / geometry helpers ----
    private float FrameToX(float f) => _laneLeft + f * _pxPerFrame - _scrollX;
    private float XToFrame(float x) => (x - _laneLeft + _scrollX) / _pxPerFrame;
    private float LaneRight => _canvasRight;
    private float CanvasBottom => _origin.Y + _size.Y;         // very bottom of the window content
    // The content area (lanes / inspector / clip page) stops above the bottom tab bar, which occupies
    // [LanesBottom, CanvasBottom] across the full width.
    private float LanesBottom => CanvasBottom - TabBarHeight;
    // Audio tracks (and the clips in them) are 20% taller than standard tracks, so heights vary and
    // the vertical layout is a running sum rather than i * TrackHeight.
    private float TrackHeightOf(int i) => Document.Tracks[i].Kind == TrackKind.Audio ? TrackHeight * AudioTrackScale : TrackHeight;
    private float TrackTop(int i)
    {
        float y = _lanesTop - _scrollY;
        for (int k = 0; k < i; k++) y += TrackHeightOf(k);
        return y;
    }
    private float TracksTotalHeight()
    {
        float h = 0f;
        for (int i = 0; i < Document.Tracks.Count; i++) h += TrackHeightOf(i);
        return h;
    }
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
        // No ImGui window background when the editor background is off, OR in the Overlay pass — there
        // we draw only the playhead/timecode over the blitted cache, so ImGui's own (dark, semi-
        // transparent) WindowBg must not paint over the cache and darken it.
        if (!ShowBackground || PlayPass == PlayCache.Overlay) flags |= ImGuiWindowFlags.NoBackground;

        IG.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        bool open = IG.Begin("Interpole — " + _title, flags);
        IG.PopStyleVar();
        if (!open) { IG.End(); return; }

        _origin = IG.GetCursorScreenPos();
        _size = IG.GetContentRegionAvail();
        if (_size.X < 64) _size.X = 64;
        if (_size.Y < 64) _size.Y = 64;
        // Resolve the clip (keyframe) page before layout (so the header width picks the right mode):
        // drop tabs whose clip vanished (deleted / undo), and if the active clip is gone fall back to
        // the timeline so nothing renders a dangling id.
        _openTabs.RemoveAll(id => FindClip(id) is null);
        if (_mode == EditorMode.Clip && (_activeClipId is null || FindClip(_activeClipId) is null)) { _mode = EditorMode.Timeline; _activeClipId = null; }

        // The header column is wider on the clip page to give float-track names more room.
        _laneLeft = _origin.X + (_mode == EditorMode.Clip ? ClipHeaderWidth : HeaderWidth);
        _rulerTop = _origin.Y + TransportHeight;
        _loopBottom = _rulerTop + LoopStripHeight;
        _rulerBottom = _rulerTop + RulerHeight;
        _lanesTop = _rulerBottom + MarkerLaneHeight;

        // The timeline area ends at _canvasRight; the open/close strip lives in the far-right
        // ToggleStripPx column, and the inspector panel (when open) between them. The clip page has no
        // inspector, so it uses the full width.
        _stripLeft = _origin.X + _size.X - ToggleStripPx;
        _canvasRight = _mode == EditorMode.Clip
            ? _origin.X + _size.X
            : MathF.Max(_laneLeft + 120f, _stripLeft - (_inspectorOpen ? InspectorWidth : 0f));

        // Allow the inspector's widgets (submitted later) to sit over the full-canvas InvisibleButton.
        IG.SetNextItemAllowOverlap();
        IG.InvisibleButton("interpole-canvas", _size);
        bool hovered = IG.IsItemHovered();
        var dl = IG.GetWindowDrawList();
        Vector2 mouse = IG.GetMousePos();

        ClampScroll();

        // Playback is advanced in Tick() (called from the node's Update, so it runs in No UI Mode too);
        // here we only keep the playhead in view while playing (needs this frame's geometry). While the
        // snapshot cache is active (a play pass) the node owns the scroll (stepped per page via SetPlayScroll),
        // so skip the edge-nudge follow — it would fight the page grid the cache keys on.
        if (_playing && !_panning && PlayPass == PlayCache.Off) EnsurePlayheadVisible();

        // Which clip is highlighted: the one being dragged, else the one under the cursor. Capture the
        // grab zone too so DrawClip can flag a resize edge before the press.
        if (_drag != DragMode.None) { _hoverClipId = _dragClipId; _hoverZone = null; }
        else
        {
            var hit = (_mode == EditorMode.Timeline && hovered && _editKind == EditKind.None && InLanes(mouse)) ? HitClip(mouse) : null;
            _hoverClipId = hit?.clip.Id;
            _hoverZone = hit?.zone;
        }

        bool overlay = PlayPass == PlayCache.Overlay;
        bool play = PlayPass != PlayCache.Off;

        if (overlay)
        {
            // Play/Overlay pass: draw ONLY the moving overlay over the blitted cache (the window is
            // NoBackground here, so nothing else is painted). Playhead + timecode readout only.
            DrawReadout(dl);
            DrawPlayhead(dl);
        }
        else
        {
            if (ShowBackground)
                dl.AddRectFilled(_origin, _origin + _size, Col(0.11f, 0.12f, 0.13f));

            // The clip (keyframe) page is the SAME view as the timeline — same time axis (_pxPerFrame /
            // _scrollX), ruler, loop ribbon, markers, playhead and transport — only the track content
            // differs: the open clip's float tracks instead of the timeline's clips. So it reuses the
            // shared chrome draws and swaps just the lanes / headers / corner.
            if (_mode == EditorMode.Clip)
            {
                EnsureKeyframeIdsAndPruneSelection();
                DrawFloatLanes(dl);
                if (_drag == DragMode.KfMarquee) DrawMarquee(dl);
                DrawRuler(dl);
                DrawLoopBar(dl);
                DrawMarkerLane(dl, mouse);
                DrawFloatHeaders(dl);
                DrawFloatTrackDragFeedback(dl);
                if (!play) { DrawPlayhead(dl); DrawSnapGuide(dl); }   // Static bakes no overlay (drawn on top)
                DrawFloatCorner(dl, mouse);
            }
            else
            {
                DrawLanes(dl);
                if (_drag == DragMode.Marquee) DrawMarquee(dl);
                DrawRuler(dl);
                DrawLoopBar(dl);
                DrawMarkerLane(dl, mouse);
                DrawHeaders(dl, mouse);
                DrawTrackDragFeedback(dl);
                if (!play) { DrawPlayhead(dl); DrawSnapGuide(dl); }
                DrawCorner(dl, mouse);
            }

            // Transport, zoom readout and the bottom tab bar are shared chrome. The inspector is
            // timeline-only. The readout (moving timecode) is baked only when NOT playing — in play the
            // Overlay pass draws it fresh over the cache.
            DrawTransport(dl, mouse);
            if (!play) DrawReadout(dl);
            DrawZoomReadout(dl);
            if (_mode == EditorMode.Timeline)
            {
                DrawInspectorStrip(dl, mouse);
                if (_inspectorOpen) DrawInspector(dl);
            }
            DrawTabBar(dl, mouse);
        }

        // Interaction. While playing, editing is locked: only transport, playhead scrub and zoom are
        // handled (HandlePlay*). Otherwise the full mode-aware editing interaction runs.
        if (play)
        {
            if (!_markerDialogOpen && !_kfMenuOpen)
            {
                HandlePlayShortcuts(io);
                HandlePlayInteraction(hovered, mouse, io);
            }
        }
        else if (_editKind != EditKind.None)
            DrawInlineEdit();
        else if (!_markerDialogOpen && !_kfMenuOpen)
        {
            HandleShortcuts(io);
            HandleInteraction(hovered, mouse, io);
        }

        // The marker create/edit modal (name + "stop playback") and the keyframe interpolation menu are
        // drawn last so they sit over the canvas and own input while open; interaction/shortcuts above
        // are suppressed meanwhile.
        DrawMarkerDialog();
        DrawKeyframeMenu();

        IG.End();
    }

    private void ClampScroll()
    {
        float contentH = TracksTotalHeight();
        float viewH = _size.Y - TransportHeight - RulerHeight - MarkerLaneHeight - TabBarHeight;
        _scrollY = Math.Clamp(_scrollY, 0f, MathF.Max(0f, contentH - viewH));
        if (_scrollX < 0) _scrollX = 0;
    }

    private void DrawLanes(ImDrawListPtr dl)
    {
        dl.PushClipRect(new Vector2(_laneLeft, _lanesTop), new Vector2(LaneRight, LanesBottom), true);

        // Row backgrounds + separators.
        for (int i = 0; i < Document.Tracks.Count; i++)
        {
            float top = TrackTop(i), bottom = top + TrackHeightOf(i);
            if (bottom < _lanesTop || top > LanesBottom) continue;
            // Audio-track rows are simply a little darker than standard rows (same neutral hue, no
            // colour) so the two kinds read apart without a tint.
            bool audio = Document.Tracks[i].Kind == TrackKind.Audio;
            uint bg = (i % 2 == 0)
                ? (audio ? Col(0.105f, 0.115f, 0.125f) : Col(0.135f, 0.145f, 0.155f))
                : (audio ? Col(0.085f, 0.095f, 0.105f) : Col(0.115f, 0.125f, 0.135f));
            dl.AddRectFilled(new Vector2(_laneLeft, top), new Vector2(LaneRight, bottom), bg);
            dl.AddLine(new Vector2(_laneLeft, bottom), new Vector2(LaneRight, bottom), Col(0f, 0f, 0f, 0.35f));
        }

        DrawTimeGrid(dl);

        // Clips on top of the grid.
        for (int i = 0; i < Document.Tracks.Count; i++)
        {
            float top = TrackTop(i);
            float th = TrackHeightOf(i);
            if (top + th < _lanesTop || top > LanesBottom) continue;
            foreach (var clip in Document.Tracks[i].Clips)
                DrawClip(dl, clip, top, th);
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

    private void DrawClip(ImDrawListPtr dl, ClipData clip, float trackTop, float trackH)
    {
        float x0 = FrameToX(clip.Start), x1 = FrameToX(ClipEnd(clip));
        if (x1 < _laneLeft || x0 > LaneRight) return;
        float y0 = trackTop + ClipPadY, y1 = trackTop + trackH - ClipPadY;
        bool hov = _hoverClipId == clip.Id;
        var (cr, cg, cb) = ClipRgb(clip.Color);
        float lift = hov ? 0.10f : 0f;   // brighten a touch on hover
        uint fill = Col(MathF.Min(1f, cr + lift), MathF.Min(1f, cg + lift), MathF.Min(1f, cb + lift));
        uint border = Col(MathF.Min(1f, cr + 0.35f), MathF.Min(1f, cg + 0.35f), MathF.Min(1f, cb + 0.35f), 0.9f);
        dl.AddRectFilled(new Vector2(x0, y0), new Vector2(x1, y1), fill, ClipRounding);

        // Audio clips draw their waveform over the fill (under the fade ramps + label). Only the
        // selected or hovered clip renders its waveform — decoding/enveloping every audio clip every
        // frame is the expensive part, so other clips get a cheap speaker badge instead.
        if (clip.IsAudio)
        {
            if (IsSelected(clip.Id) || _hoverClipId == clip.Id) DrawWaveform(dl, clip, x0, x1, y0, y1);
            else DrawAudioBadge(dl, x0, x1, y0, y1);
        }

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

        // Resize-edge cue: a bright vertical grip on the in/out edge when the cursor is over that
        // edge (or while it's being dragged), so it's clear a drag will trim the clip, not move it.
        // (The host's ImGui backend doesn't drive the OS cursor, so we signal it in-canvas instead.)
        bool draggingThis = _dragClipId == clip.Id;
        bool gripL = (hov && _drag == DragMode.None && _hoverZone == ClipZone.ResizeL) || (draggingThis && _drag == DragMode.ResizeL);
        bool gripR = (hov && _drag == DragMode.None && _hoverZone == ClipZone.ResizeR) || (draggingThis && _drag == DragMode.ResizeR);
        if (gripL) DrawEdgeGrip(dl, x0, y0, y1, true);
        if (gripR) DrawEdgeGrip(dl, x1, y0, y1, false);
    }

    // A bright vertical grip hugging the inside of a clip's in/out edge — the resize affordance.
    private void DrawEdgeGrip(ImDrawListPtr dl, float edgeX, float y0, float y1, bool left)
    {
        const float w = 3.5f;
        float gx0 = left ? edgeX : edgeX - w;
        float gx1 = left ? edgeX + w : edgeX;
        dl.AddRectFilled(new Vector2(gx0, y0 + 1f), new Vector2(gx1, y1 - 1f), Col(1f, 1f, 1f, 0.9f),
                         ClipRounding, left ? ImDrawFlags.RoundCornersLeft : ImDrawFlags.RoundCornersRight);
    }

    // Cheap "this is audio" cue for clips whose waveform isn't rendered: a small speaker glyph
    // centered in the clip. A few DrawList primitives, constant cost per clip — no peak decode /
    // envelope work.
    private void DrawAudioBadge(ImDrawListPtr dl, float x0, float x1, float y0, float y1)
    {
        if (x1 - x0 < 22f || y1 - y0 < 14f) return;   // too small to read; skip
        float ax = (x0 + x1) * 0.5f - 7.5f;           // center the glyph (spans ~15px wide)
        float cy = (y0 + y1) * 0.5f;
        uint col = Col(1f, 1f, 1f, 0.55f);
        // Driver box + cone (throat at the box, widening to the right).
        dl.AddRectFilled(new Vector2(ax, cy - 2.5f), new Vector2(ax + 3f, cy + 2.5f), col);
        dl.AddTriangleFilled(new Vector2(ax + 3f, cy), new Vector2(ax + 8f, cy - 5f), new Vector2(ax + 8f, cy + 5f), col);
        // Two sound-wave arcs radiating from the cone mouth.
        dl.PathArcTo(new Vector2(ax + 8f, cy), 4.5f, -0.9f, 0.9f, 8);
        dl.PathStroke(col, ImDrawFlags.None, 1.2f);
        dl.PathArcTo(new Vector2(ax + 8f, cy), 7f, -0.9f, 0.9f, 10);
        dl.PathStroke(col, ImDrawFlags.None, 1.2f);
    }

    private void DrawFadeHandle(ImDrawListPtr dl, ClipData clip, bool fadeIn, float x0, float x1, float y0, uint fillCol)
    {
        var (hmin, hmax) = FadeHandleRect(clip, fadeIn, x0, x1, y0);
        dl.AddRectFilled(hmin, hmax, fillCol, 2f);
        dl.AddRect(hmin, hmax, Col(0f, 0f, 0f, 0.55f), 2f);
    }

    // Per-clip cache of the waveform's per-column min/max envelope, so it isn't recomputed every
    // frame. The envelope as a function of clip-relative pixel is invariant to scroll/position — it
    // depends only on the file's peaks, the trim (FileInPoint), the zoom (pxPerFrame), Fps and the
    // clip length — so we rebuild only when one of those changes. Columns outside the file hold NaN.
    private sealed class WaveEnvelope
    {
        public double FileInPoint;
        public float PxPerFrame;
        public int Fps;
        public int LengthFrames;
        public WaveformPeaks? Peaks;
        public float[] Min = Array.Empty<float>();   // Min[d] = NaN → that column is outside the file
        public float[] Max = Array.Empty<float>();
    }
    private readonly Dictionary<string, WaveEnvelope> _waveEnv = new();
    // Above this width we don't cache: a very long clip zoomed in would need a huge array, and in that
    // regime each column reads ~one bucket, so the direct path is already cheap. The cache earns its
    // keep zoomed out, where the clip is few pixels wide but each column sums many buckets.
    private const int WaveEnvMaxColumns = 100_000;

    // The cached column envelope for a clip, rebuilt only when its key changes; null when the clip is
    // too wide to cache (draw it directly then — see DrawWaveform).
    private WaveEnvelope? GetWaveEnvelope(ClipData clip, WaveformPeaks peaks)
    {
        int cols = (int)MathF.Ceiling(clip.Length * _pxPerFrame) + 1;
        if (cols > WaveEnvMaxColumns) { _waveEnv.Remove(clip.Id); return null; }

        if (!_waveEnv.TryGetValue(clip.Id, out var e)) { e = new WaveEnvelope(); _waveEnv[clip.Id] = e; }
        bool fresh = ReferenceEquals(e.Peaks, peaks) && e.FileInPoint == clip.FileInPoint
                  && e.PxPerFrame == _pxPerFrame && e.Fps == _fps && e.LengthFrames == clip.Length
                  && e.Min.Length == cols;
        if (fresh) return e;

        if (e.Min.Length != cols) { e.Min = new float[cols]; e.Max = new float[cols]; }
        double secPerPx = 1.0 / (_pxPerFrame * _fps);   // file-seconds spanned by one screen pixel
        for (int d = 0; d < cols; d++)
        {
            double tA = clip.FileInPoint + d * secPerPx;
            if (peaks.Envelope(tA, tA + secPerPx, out float mn, out float mx)) { e.Min[d] = mn; e.Max[d] = mx; }
            else e.Min[d] = float.NaN;
        }
        e.Peaks = peaks; e.FileInPoint = clip.FileInPoint; e.PxPerFrame = _pxPerFrame; e.Fps = _fps; e.LengthFrames = clip.Length;
        return e;
    }

    // Draw an audio clip's waveform as a per-pixel min/max envelope. Each screen column maps to the
    // file-time window it covers (from the clip's FileInPoint, at natural rate) and draws a vertical
    // line between that window's min and max sample. Columns past the file's end (a clip longer than
    // its media) are left blank. While the peaks are still decoding — or if the file is missing — a
    // small status label is shown instead. The peak data comes from the async WaveformCache; the
    // per-column envelope from the frame-to-frame cache above (GetWaveEnvelope).
    private void DrawWaveform(ImDrawListPtr dl, ClipData clip, float x0, float x1, float y0, float y1)
    {
        var state = WaveformCache.Get(clip.FilePath!, out var peaks);
        float midY = (y0 + y1) * 0.5f;
        float halfH = (y1 - y0) * 0.5f - 2f;

        if (state != WaveformState.Ready || peaks is null)
        {
            string msg = state == WaveformState.Failed ? "! missing file" : "loading...";
            var ts = IG.CalcTextSize(msg);
            dl.AddText(new Vector2((x0 + x1) * 0.5f - ts.X * 0.5f, midY - ts.Y * 0.5f), Col(1f, 1f, 1f, 0.5f), msg);
            return;
        }

        float vx0 = MathF.Max(x0, _laneLeft);
        float vx1 = MathF.Min(x1, LaneRight);
        uint wcol = Col(1f, 1f, 1f, 0.5f);

        var env = GetWaveEnvelope(clip, peaks);
        if (env is not null)
        {
            // Index the cache by clip-relative pixel (d = x − clip-left), so scrolling never rebuilds it.
            for (float x = vx0; x <= vx1; x += 1f)
            {
                int d = (int)(x - x0 + 0.5f);
                if (d < 0 || d >= env.Min.Length) continue;
                float mn = env.Min[d];
                if (float.IsNaN(mn)) continue;   // column outside the file
                float yTop = midY - env.Max[d] * halfH;
                float yBot = midY - mn * halfH;
                if (yBot - yTop < 1f) { yTop -= 0.5f; yBot += 0.5f; }   // keep near-silence visible
                dl.AddLine(new Vector2(x, yTop), new Vector2(x, yBot), wcol, 1f);
            }
            return;
        }

        // Uncached fallback (clip too wide to cache; cheap in that zoomed-in regime).
        double secPerPx = 1.0 / (_pxPerFrame * _fps);
        for (float x = vx0; x <= vx1; x += 1f)
        {
            double tA = clip.FileInPoint + (XToFrame(x) - clip.Start) / (double)_fps;
            if (!peaks.Envelope(tA, tA + secPerPx, out float mn, out float mx)) continue;
            float yTop = midY - mx * halfH;
            float yBot = midY - mn * halfH;
            if (yBot - yTop < 1f) { yTop -= 0.5f; yBot += 0.5f; }
            dl.AddLine(new Vector2(x, yTop), new Vector2(x, yBot), wcol, 1f);
        }
    }

    // Effective (clamped) fade lengths, robust to out-of-range persisted values. Delegates to the
    // Engine so the editor's ramps and the node's Fade Gain output clamp identically.
    private static (int fadeIn, int fadeOut) Fades(ClipData c) => c.EffectiveFades();

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
        dl.PushClipRect(new Vector2(_laneLeft, _rulerTop), new Vector2(LaneRight, _rulerBottom), true);
        dl.AddRectFilled(new Vector2(_laneLeft, _rulerTop), new Vector2(LaneRight, _rulerBottom), Col(0.14f, 0.15f, 0.17f));

        // In timecode mode use a whole-second step so labels stay hh:mm:ss (no frame subfield,
        // keeps the ruler uncramped); in frames mode step on nice frame counts.
        int step = _showTimecode
            ? NiceSecondStep((80f / _pxPerFrame) / _fps) * _fps
            : NiceFrameStep(80f / _pxPerFrame);
        int first = (int)MathF.Floor(XToFrame(_laneLeft) / step) * step;
        if (first < 0) first = 0;
        for (int f = first; ; f += step)
        {
            float x = FrameToX(f);
            if (x > LaneRight) break;
            if (x < _laneLeft) continue;
            dl.AddLine(new Vector2(x, _rulerBottom - 8), new Vector2(x, _rulerBottom), Col(0.5f, 0.55f, 0.6f));
            // Vertically center the label in the ruler *below* the loop strip so the two don't collide.
            var lbl = _showTimecode ? FramesToTimecode(f, step) : f.ToString(CultureInfo.InvariantCulture);
            float ty = _loopBottom + (_rulerBottom - _loopBottom - IG.CalcTextSize(lbl).Y) * 0.5f;
            dl.AddText(new Vector2(x + 4, ty), Col(0.7f, 0.75f, 0.8f), lbl);
        }

        // Above 100% zoom, mark each individual frame with a small dot between the labelled ticks, so
        // single frames become readable. Since a frame is > DefaultPxPerFrame px apart here, the dots
        // spread further (higher resolution) the more you zoom in.
        if (_pxPerFrame > DefaultPxPerFrame)
        {
            float dy = _rulerBottom - 3f;
            uint dcol = Col(0.5f, 0.55f, 0.6f, 0.65f);
            for (int f = Math.Max(0, (int)MathF.Floor(XToFrame(_laneLeft))); ; f++)
            {
                float x = FrameToX(f);
                if (x > LaneRight) break;
                if (x < _laneLeft || f % step == 0) continue;   // skip off-screen + already-labelled ticks
                dl.AddCircleFilled(new Vector2(x, dy), 1.2f, dcol);
            }
        }

        dl.AddLine(new Vector2(_laneLeft, _rulerBottom), new Vector2(LaneRight, _rulerBottom), Col(0f, 0f, 0f, 0.5f));
        dl.PopClipRect();
    }

    // The loop-range ribbon, carved from the top strip of the ruler [_rulerTop, _loopBottom): a green
    // bar spanning the effective loop region with a draggable grip at each end. Drawn brighter when
    // the range is explicitly authored, faint when it's the implicit whole-content default.
    private void DrawLoopBar(ImDrawListPtr dl)
    {
        dl.PushClipRect(new Vector2(_laneLeft, _rulerTop), new Vector2(LaneRight, _loopBottom), true);
        dl.AddRectFilled(new Vector2(_laneLeft, _rulerTop), new Vector2(LaneRight, _loopBottom), Col(0.10f, 0.11f, 0.12f));

        var (li, lo) = Document.EffectiveLoop();
        bool explicitRange = Document.LoopOut > Math.Max(0, Document.LoopIn);
        if (lo > li)
        {
            float xi = FrameToX(li), xo = FrameToX(lo);
            float y0 = _rulerTop + 2f, y1 = _loopBottom - 2f;
            uint bar = explicitRange ? Col(0.30f, 0.72f, 0.45f, 0.85f) : Col(0.30f, 0.72f, 0.45f, 0.28f);
            dl.AddRectFilled(new Vector2(MathF.Max(xi, _laneLeft), y0), new Vector2(MathF.Min(xo, LaneRight), y1), bar, 2f);
            uint grip = Col(0.62f, 0.96f, 0.72f, 0.95f);
            DrawLoopHandle(dl, xi, y0, y1, grip);
            DrawLoopHandle(dl, xo, y0, y1, grip);
        }
        dl.PopClipRect();
        dl.AddLine(new Vector2(_laneLeft, _loopBottom), new Vector2(LaneRight, _loopBottom), Col(0f, 0f, 0f, 0.35f));
    }

    private void DrawLoopHandle(ImDrawListPtr dl, float x, float y0, float y1, uint col)
    {
        if (x < _laneLeft - 3f || x > LaneRight + 3f) return;
        dl.AddRectFilled(new Vector2(x - 2f, y0), new Vector2(x + 2f, y1), col, 1.5f);
    }

    private void DrawPlayhead(ImDrawListPtr dl)
    {
        float x = FrameToX(Document.Playhead);
        if (x < _laneLeft || x > LaneRight) return;
        // Clip to the ruler-below-loop-strip + lanes (never over the loop strip, header, or transport).
        dl.PushClipRect(new Vector2(_laneLeft, _loopBottom), new Vector2(LaneRight, LanesBottom), true);
        // Snapped (Shift-scrub) turns the playhead cyan and a touch thicker as feedback.
        bool snapped = _drag == DragMode.Playhead && _playheadSnapped;
        uint col = snapped ? Col(0.30f, 0.85f, 0.98f) : Col(0.95f, 0.32f, 0.30f);
        dl.AddLine(new Vector2(x, _loopBottom + 12), new Vector2(x, LanesBottom), col, snapped ? 2.5f : 1.5f);
        // Downward-pointing handle sitting in the ruler, just below the loop strip.
        const float hw = 7f, top = 1f;
        dl.AddTriangleFilled(
            new Vector2(x - hw, _loopBottom + top),
            new Vector2(x + hw, _loopBottom + top),
            new Vector2(x, _loopBottom + top + 13f), col);
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

    private void DrawHeaders(ImDrawListPtr dl, Vector2 mouse)
    {
        dl.PushClipRect(new Vector2(_origin.X, _lanesTop), new Vector2(_laneLeft, LanesBottom), true);
        dl.AddRectFilled(new Vector2(_origin.X, _lanesTop), new Vector2(_laneLeft, LanesBottom), Col(0.15f, 0.16f, 0.18f));

        for (int i = 0; i < Document.Tracks.Count; i++)
        {
            float top = TrackTop(i), bottom = top + TrackHeightOf(i);
            if (bottom < _lanesTop || top > LanesBottom) continue;
            var tr = Document.Tracks[i];
            bool audio = tr.Kind == TrackKind.Audio;
            uint bg = (i % 2 == 0)
                ? (audio ? Col(0.140f, 0.150f, 0.170f) : Col(0.17f, 0.18f, 0.20f))
                : (audio ? Col(0.120f, 0.130f, 0.150f) : Col(0.15f, 0.16f, 0.18f));
            dl.AddRectFilled(new Vector2(_origin.X, top), new Vector2(_laneLeft, bottom), bg);
            dl.AddLine(new Vector2(_origin.X, bottom), new Vector2(_laneLeft, bottom), Col(0f, 0f, 0f, 0.35f));
            // accent tab — neutral grey for audio tracks (distinguished only by their darker rows),
            // blue for standard.
            uint accent = tr.Kind == TrackKind.Audio ? Col(0.42f, 0.44f, 0.48f) : Col(0.25f, 0.55f, 0.85f);
            dl.AddRectFilled(new Vector2(_origin.X, top), new Vector2(_origin.X + 5, bottom), accent);
            string name = string.IsNullOrWhiteSpace(tr.Name) ? tr.Id : tr.Name;
            var ts = IG.CalcTextSize(name);
            float nameY = (top + bottom) * 0.5f - ts.Y * 0.5f;
            // Mute / Solo buttons — audio tracks only, right-aligned and level with the name. M lights
            // red when muted, S yellow when soloed. Clip the name so it can't run under the buttons.
            float nameRight = _laneLeft - 6f;
            if (audio)
            {
                var (mMin, mMax, sMin, sMax) = TrackToggleRects(i);
                nameRight = mMin.X - 6f;
                DrawTrackToggle(dl, mMin, mMax, tr.Mute, InRect(mouse, mMin, mMax), Col(0.78f, 0.26f, 0.24f), "M");
                DrawTrackToggle(dl, sMin, sMax, tr.Solo, InRect(mouse, sMin, sMax), Col(0.82f, 0.72f, 0.22f), "S");
            }
            dl.PushClipRect(new Vector2(_origin.X + 14, top), new Vector2(nameRight, bottom), true);
            dl.AddText(new Vector2(_origin.X + 14, nameY), Col(0.9f, 0.94f, 1f), name);
            dl.PopClipRect();
        }
        // column divider
        dl.AddLine(new Vector2(_laneLeft, _lanesTop), new Vector2(_laneLeft, LanesBottom), Col(0f, 0f, 0f, 0.5f));
        dl.PopClipRect();
    }

    // The Mute / Solo button rects in an audio track's header — a right-aligned pair (M then S), level
    // with the track name. Only meaningful for audio tracks; used by both the draw and the hit-testing.
    private (Vector2 mMin, Vector2 mMax, Vector2 sMin, Vector2 sMax) TrackToggleRects(int i)
    {
        const float bh = 30f, gap = 4f, margin = 8f;
        float y0 = TrackTop(i) + TrackHeightOf(i) * 0.5f - bh * 0.5f;
        float sx1 = _laneLeft - margin;   // S right edge (rightmost)
        float sx0 = sx1 - bh;
        float mx1 = sx0 - gap;
        float mx0 = mx1 - bh;
        return (new Vector2(mx0, y0), new Vector2(mx1, y0 + bh),
                new Vector2(sx0, y0), new Vector2(sx1, y0 + bh));
    }

    private void DrawTrackToggle(ImDrawListPtr dl, Vector2 mn, Vector2 mx, bool on, bool hov, uint onColor, string label)
    {
        uint bg = on ? onColor : hov ? Col(0.30f, 0.32f, 0.36f) : Col(0.22f, 0.24f, 0.27f);
        dl.AddRectFilled(mn, mx, bg, 3f);
        dl.AddRect(mn, mx, Col(0f, 0f, 0f, 0.4f), 3f);
        uint ic = on ? Col(0.08f, 0.06f, 0.04f) : Col(0.85f, 0.88f, 0.92f);   // dark letter on the lit fill
        var c = (mn + mx) * 0.5f;
        var ts = IG.CalcTextSize(label);
        dl.AddText(new Vector2(c.X - ts.X * 0.5f, c.Y - ts.Y * 0.5f), ic, label);
    }

    // While a track header is being dragged to reorder, tint its (live-reflowed) row across the full
    // width and draw a bright insertion line at its top and bottom edges so the drop slot is obvious.
    private void DrawTrackDragFeedback(ImDrawListPtr dl)
    {
        if (_drag != DragMode.MoveTrack) return;
        int i = Document.Tracks.FindIndex(t => t.Id == _dragTrackId);
        if (i < 0) return;
        float top = TrackTop(i), bottom = top + TrackHeightOf(i);
        if (bottom < _lanesTop || top > LanesBottom) return;
        float ct = MathF.Max(top, _lanesTop), cb = MathF.Min(bottom, LanesBottom);   // clamp into the lanes viewport
        dl.AddRectFilled(new Vector2(_origin.X, ct), new Vector2(LaneRight, cb), Col(0.30f, 0.55f, 0.85f, 0.16f));
        uint line = Col(0.35f, 0.70f, 1f, 0.95f);
        if (top >= _lanesTop) dl.AddLine(new Vector2(_origin.X, top), new Vector2(LaneRight, top), line, 2f);
        if (bottom <= LanesBottom) dl.AddLine(new Vector2(_origin.X, bottom), new Vector2(LaneRight, bottom), line, 2f);
    }

    // The clip page's twin of DrawTrackDragFeedback: while a float-track header is dragged to reorder,
    // tint its (live-reflowed) row and draw insertion lines at its edges so the drop slot is obvious.
    private void DrawFloatTrackDragFeedback(ImDrawListPtr dl)
    {
        if (_drag != DragMode.MoveFloatTrack) return;
        if (FindClip(_activeClipId) is not { } fc) return;
        int i = fc.clip.FloatTracks.FindIndex(t => t.Id == _dragTrackId);
        if (i < 0) return;
        float top = FloatRowTop(i), bottom = top + TrackHeight;
        if (bottom < _lanesTop || top > LanesBottom) return;
        float ct = MathF.Max(top, _lanesTop), cb = MathF.Min(bottom, LanesBottom);
        dl.AddRectFilled(new Vector2(_origin.X, ct), new Vector2(LaneRight, cb), Col(0.30f, 0.55f, 0.85f, 0.16f));
        uint line = Col(0.35f, 0.70f, 1f, 0.95f);
        if (top >= _lanesTop) dl.AddLine(new Vector2(_origin.X, top), new Vector2(LaneRight, top), line, 2f);
        if (bottom <= LanesBottom) dl.AddLine(new Vector2(_origin.X, bottom), new Vector2(LaneRight, bottom), line, 2f);
    }

    private void DrawCorner(ImDrawListPtr dl, Vector2 mouse)
    {
        dl.AddRectFilled(new Vector2(_origin.X, _rulerTop), new Vector2(_laneLeft, _rulerBottom), Col(0.13f, 0.14f, 0.16f));
        // Two "add track" buttons side by side: a standard clip track and an audio track. Audio
        // tracks are tinted green to match their header accent + default clip colour.
        DrawCornerButton(dl, mouse, AddTrackButtonRect(), "+ Track", Col(0.22f, 0.4f, 0.6f), Col(0.28f, 0.5f, 0.75f));
        DrawCornerButton(dl, mouse, AddAudioTrackButtonRect(), "+ Audio", Col(0.20f, 0.5f, 0.42f), Col(0.26f, 0.62f, 0.52f));
    }

    private void DrawCornerButton(ImDrawListPtr dl, Vector2 mouse, (Vector2 min, Vector2 max) r, string label, uint bg, uint bgHov)
    {
        bool hov = InRect(mouse, r.min, r.max);
        dl.AddRectFilled(r.min, r.max, hov ? bgHov : bg, 4f);
        var ts = IG.CalcTextSize(label);
        dl.AddText(new Vector2((r.min.X + r.max.X) * 0.5f - ts.X * 0.5f, (r.min.Y + r.max.Y) * 0.5f - ts.Y * 0.5f),
                   Col(0.95f, 0.98f, 1f), label);
    }

    // Current time-axis zoom, shown bottom-right of the lanes (px per frame).
    private void DrawZoomReadout(ImDrawListPtr dl)
    {
        int pct = (int)MathF.Round(_pxPerFrame / DefaultPxPerFrame * 100f);
        string txt = "zoom " + pct + "%";
        var ts = IG.CalcTextSize(txt);
        dl.AddText(new Vector2(LaneRight - ts.X - 8, LanesBottom - ts.Y - 6), Col(0.5f, 0.55f, 0.6f), txt);
    }

    // The corner splits into two buttons: "+ Track" (left, wider) and "+ Audio" (right). The split
    // point is shared so the two rects abut with a small gap.
    private float CornerSplitX => _origin.X + 8 + (_laneLeft - 16 - (_origin.X + 8)) * 0.56f;
    private (Vector2 min, Vector2 max) AddTrackButtonRect()
        => (new Vector2(_origin.X + 8, _rulerTop + 4), new Vector2(CornerSplitX - 3, _rulerBottom - 4));
    private (Vector2 min, Vector2 max) AddAudioTrackButtonRect()
        => (new Vector2(CornerSplitX + 3, _rulerTop + 4), new Vector2(_laneLeft - 8, _rulerBottom - 4));

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

        // A selected marker owns the panel (mutually exclusive with a clip selection).
        var selMarker = FindMarker(_selectedMarkerId);
        if (selMarker is not null) { MarkerInspector(selMarker); return; }

        if (_selection.Count > 1) { IG.Spacing(); IG.TextDisabled(_selection.Count + " clips selected — editing primary"); IG.Spacing(); }

        var found = FindClip(_selectedClipId);
        if (found is null) { IG.Spacing(); IG.TextDisabled("Nothing selected.\nClick a clip or a marker, or drag a box to select several clips."); return; }
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

        // Audio-clip fields: the source file (read-only), the trim in-point, and a linear gain.
        if (clip.IsAudio)
        {
            IG.Spacing();
            IG.Separator();
            IG.TextDisabled("AUDIO");

            IG.Text("File");
            IG.SetNextItemWidth(-1f);
            string fileName = System.IO.Path.GetFileName(clip.FilePath) ?? "";
            IG.InputText("##inspFile", ref fileName, 260, ImGuiInputTextFlags.ReadOnly);

            IG.Text("In-point (s)");
            IG.SetNextItemWidth(-1f);
            float inSec = (float)clip.FileInPoint;
            if (IG.DragFloat("##inspInPoint", ref inSec, 0.01f, 0f, 0f, "%.3f s")) clip.FileInPoint = Math.Max(0, inSec);
            if (IG.IsItemDeactivatedAfterEdit()) Commit();

            IG.Text("Gain");
            IG.SetNextItemWidth(-1f);
            float gain = clip.Gain;
            if (IG.DragFloat("##inspGain", ref gain, 0.01f, 0f, 8f, "%.2f")) clip.Gain = Math.Max(0f, gain);
            if (IG.IsItemDeactivatedAfterEdit()) Commit();
        }

        IG.Spacing();
        IG.Separator();
        // Opens this clip's keyframe editor page (a bottom tab appears; the canvas switches to it).
        // Audio clips have no keyframes yet, so no entry for them.
        if (!clip.IsAudio && IG.Button("Edit Keyframes  ›", new Vector2(-1f, 0f)))
            OpenClipKeyframes(clip);

        IG.Spacing();
        IG.TextDisabled("track: " + (string.IsNullOrWhiteSpace(tr.Name) ? tr.Id : tr.Name));
    }

    // The inspector for a selected marker: name, colour (shared clip palette), frame, stop flag, delete.
    // Same commit conventions as the clip inspector — live preview into the marker, one undo step when
    // an edit finishes. Re-seeds the shared name buffer whenever the field isn't being typed in.
    private void MarkerInspector(MarkerData m)
    {
        IG.TextDisabled("MARKER");
        IG.Spacing();

        IG.Text("Name");
        IG.SetNextItemWidth(-1f);
        if (!_inspNameActive || _inspNameId != m.Id) { _inspName = m.Name ?? ""; _inspNameId = m.Id; }
        if (IG.InputText("##inspMkName", ref _inspName, 128)) m.Name = _inspName.Trim();
        _inspNameActive = IG.IsItemActive();
        if (IG.IsItemDeactivatedAfterEdit()) Commit();

        IG.Spacing();
        IG.Text("Color");
        for (int i = 0; i < ClipPalette.Length; i++)
        {
            if (i % 5 != 0) IG.SameLine();
            var (r, g, b) = ClipPalette[i];
            if (IG.ColorButton($"##inspMkCol{i}", new Vector4(r, g, b, 1f),
                    ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new Vector2(30f, 24f)))
            { m.Color = i; Commit(); }
            if (m.Color == i)
                IG.GetWindowDrawList().AddRect(IG.GetItemRectMin(), IG.GetItemRectMax(),
                    Col(1f, 0.93f, 0.5f, 1f), 2f, ImDrawFlags.None, 2.5f);
        }

        IG.Spacing();
        IG.Separator();
        bool timeMode = _showTimecode;   // frame field follows the ruler's frames/timecode mode
        IG.TextDisabled(timeMode ? "seconds (at " + Fps + " fps)" : "frames");
        var fr = NumberField("Frame", "mkframe", m.Frame, timeMode, 0);
        if (fr.changed) m.Frame = fr.value;
        if (fr.done) Commit();

        IG.Spacing();
        IG.TextDisabled("On reach");

        // Stop and Jump are mutually exclusive actions: setting one clears the other.
        bool stop = m.Stop;
        if (IG.Checkbox("Stop playback when reached", ref stop))
        {
            m.Stop = stop;
            if (stop) m.JumpTargetId = "";
            Commit();
        }

        // Jump: seek to another marker and keep playing. A backward target loops forever until some
        // other event (Stop / Automata state change / Set / Rewind) moves the playhead elsewhere.
        IG.Text("Jump to marker");
        IG.SetNextItemWidth(-1f);
        if (IG.BeginCombo("##inspMkJump", MarkerLabel(FindMarker(m.JumpTargetId))))
        {
            if (IG.Selectable("(none)", string.IsNullOrEmpty(m.JumpTargetId)))
            { m.JumpTargetId = ""; Commit(); }
            foreach (var t in Document.Markers)
            {
                if (t.Id == m.Id) continue;   // a marker can't jump to itself
                if (IG.Selectable(MarkerLabel(t) + "##" + t.Id, m.JumpTargetId == t.Id))
                { m.JumpTargetId = t.Id; m.Stop = false; Commit(); }
            }
            IG.EndCombo();
        }

        IG.Spacing();
        IG.Separator();
        if (IG.Button("Delete marker", new Vector2(-1f, 0f))) { RemoveMarker(m); _selectedMarkerId = null; }
    }

    // A human label for a marker in menus/combos: its name, or a frame-tagged placeholder when unnamed,
    // or "(none)" for a missing/unset reference. Used by the marker inspector's Jump-to dropdown.
    private static string MarkerLabel(MarkerData? m)
        => m is null ? "(none)"
         : !string.IsNullOrWhiteSpace(m.Name) ? m.Name
         : "(unnamed @" + m.Frame + ")";

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

    // The larger baked font used for the playhead readout (font index 1 — see InterpoleNode.EditorFonts).
    // Falls back to the default font when only one font is baked (e.g. no custom fonts on non-Windows).
    private static ImFontPtr ReadoutFont()
    {
        var fonts = IG.GetIO().Fonts.Fonts;
        return fonts.Size > 1 ? fonts[1] : IG.GetFont();
    }

    private void DrawTransport(ImDrawListPtr dl, Vector2 mouse)
    {
        dl.AddRectFilled(_origin, new Vector2(LaneRight, _rulerTop), Col(0.16f, 0.17f, 0.19f));
        dl.AddLine(new Vector2(_origin.X, _rulerTop), new Vector2(LaneRight, _rulerTop), Col(0f, 0f, 0f, 0.5f));

        for (int i = 0; i < 4; i++)
        {
            var (bmin, bmax) = TransportButtonRect(i);
            bool hov = InRect(mouse, bmin, bmax);
            // Loop and Play both show a green "engaged" background when on (Loop enabled / currently
            // playing), so the Play button reads as a toggle.
            bool active = (i == (int)TButton.Loop && _loop) || (i == (int)TButton.Play && _playing);
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

    // The playhead readout on the left of the transport bar — the value only: timecode in time mode,
    // else a zero-padded 6-digit frame counter (e.g. 000042). Split out of DrawTransport because it is
    // the one bit of the transport that ANIMATES: when playing it is drawn fresh over the cached still
    // (the Overlay pass), so the timecode keeps ticking without re-rendering the whole editor. A patch
    // in the bar colour is painted first so the Overlay pass cleanly overwrites the cache's (blank)
    // readout area; the patch stops short of the first transport button so it never covers the buttons.
    private void DrawReadout(ImDrawListPtr dl)
    {
        string txt = _showTimecode
            ? FramesToTimecode(Document.Playhead, 1)   // step 1 => always show the frame subfield
            : Document.Playhead.ToString("000000", CultureInfo.InvariantCulture);
        // Draw with the larger baked readout font (index 1) at its native size — crisp, where scaling
        // the default font up was pixelated. ReadoutTextScale still trims it a touch if needed.
        var font = ReadoutFont();
        float sz = font.FontSize * ReadoutTextScale;
        var ts = font.CalcTextSizeA(sz, float.MaxValue, 0f, txt);
        float patchRight = TransportButtonRect(0).min.X - 8f;
        dl.AddRectFilled(_origin, new Vector2(patchRight, _rulerTop), Col(0.16f, 0.17f, 0.19f));
        dl.AddText(font, sz, new Vector2(_origin.X + ReadoutPadX, _origin.Y + (TransportHeight - ts.Y) * 0.5f), Col(0.85f, 0.9f, 0.95f), txt);
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
        float s = MathF.Min(bmax.X - bmin.X, bmax.Y - bmin.Y) * 0.21f;
        uint col = Col(0.95f, 0.97f, 1f);
        switch ((TButton)i)
        {
            case TButton.Play: // right-pointing triangle, tinted green while playing (toggle-on cue)
                uint pc = _playing ? Col(0.85f, 1f, 0.9f) : col;
                dl.AddTriangleFilled(new Vector2(c.X - s, c.Y - s), new Vector2(c.X - s, c.Y + s), new Vector2(c.X + s, c.Y), pc);
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

    // ============================== bottom tab bar + clip page ==============================

    // Draws the [Timeline] tab plus one [◆ ClipName ✕] tab per opened clip along the very bottom. Tabs
    // are rounded on their BOTTOM corners so they read as hanging from the content above. Hit rects are
    // cached in _tabHits for TryHandleTabBarClick (draw runs before interaction each frame).
    private void DrawTabBar(ImDrawListPtr dl, Vector2 mouse)
    {
        float top = LanesBottom, bottom = CanvasBottom, right = _origin.X + _size.X;
        dl.AddRectFilled(new Vector2(_origin.X, top), new Vector2(right, bottom), Col(0.10f, 0.11f, 0.12f));
        dl.AddLine(new Vector2(_origin.X, top), new Vector2(right, top), Col(0f, 0f, 0f, 0.5f));

        const float padX = 16f, gap = 3f, tabTopInset = 3f;
        float textY = top + (bottom - top) * 0.5f + 1f;   // vertical centre for text / diamond / ✕
        float x = _origin.X + 8f;

        // Timeline tab (always present).
        {
            const string lbl = "Timeline";
            var ts = IG.CalcTextSize(lbl);
            float w = ts.X + padX * 2f;
            _tlTabMin = new Vector2(x, top + tabTopInset);
            _tlTabMax = new Vector2(x + w, bottom);
            bool active = _mode == EditorMode.Timeline;
            DrawTabBackground(dl, mouse, _tlTabMin, _tlTabMax, active);
            dl.AddText(new Vector2(x + padX, top + (bottom - top - ts.Y) * 0.5f + 1f),
                       active ? Col(0.92f, 0.95f, 1f) : Col(0.60f, 0.64f, 0.70f), lbl);
            x += w + gap;
        }

        // One tab per opened clip (in open order). Each has a colour diamond, name and a ✕ close.
        _tabHits.Clear();
        foreach (var id in _openTabs)
        {
            if (FindClip(id) is not { } f) continue;
            string name = string.IsNullOrWhiteSpace(f.clip.Label) ? "clip" : f.clip.Label;
            var ts = IG.CalcTextSize(name);
            const float closeW = 24f, diamondW = 12f;
            float w = padX + diamondW + ts.X + closeW;
            if (x + w > right - 8f) break;   // out of room (no tab-strip scrolling yet)
            var tabMin = new Vector2(x, top + tabTopInset);
            var tabMax = new Vector2(x + w, bottom);
            bool active = _mode == EditorMode.Clip && _activeClipId == id;
            DrawTabBackground(dl, mouse, tabMin, tabMax, active);

            var (cr, cg, cb) = ClipRgb(f.clip.Color);
            float dx = x + padX * 0.5f + 3f, dr = 4.5f;
            dl.AddQuadFilled(new Vector2(dx, textY - dr), new Vector2(dx + dr, textY),
                             new Vector2(dx, textY + dr), new Vector2(dx - dr, textY), Col(cr, cg, cb));
            float tx = dx + dr + 6f;
            dl.AddText(new Vector2(tx, top + (bottom - top - ts.Y) * 0.5f + 1f),
                       active ? Col(0.92f, 0.95f, 1f) : Col(0.66f, 0.70f, 0.76f), name);

            float clcx = x + w - closeW * 0.5f;
            var closeMin = new Vector2(clcx - 9f, textY - 9f);
            var closeMax = new Vector2(clcx + 9f, textY + 9f);
            uint xcol = InRect(mouse, closeMin, closeMax) ? Col(1f, 0.72f, 0.72f) : Col(0.6f, 0.64f, 0.7f);
            const float xh = 4f;
            dl.AddLine(new Vector2(clcx - xh, textY - xh), new Vector2(clcx + xh, textY + xh), xcol, 1.6f);
            dl.AddLine(new Vector2(clcx + xh, textY - xh), new Vector2(clcx - xh, textY + xh), xcol, 1.6f);

            _tabHits.Add((id, tabMin, tabMax, closeMin, closeMax));
            x += w + gap;
        }
    }

    private void DrawTabBackground(ImDrawListPtr dl, Vector2 mouse, Vector2 min, Vector2 max, bool active)
    {
        bool hov = InRect(mouse, min, max);
        uint bg = active ? Col(0.20f, 0.22f, 0.26f) : hov ? Col(0.155f, 0.165f, 0.19f) : Col(0.125f, 0.135f, 0.155f);
        dl.AddRectFilled(min, max, bg, 5f, ImDrawFlags.RoundCornersBottom);
        // Active tab: a bright accent line along its top edge, tying it to the content above.
        if (active) dl.AddLine(new Vector2(min.X + 2f, min.Y), new Vector2(max.X - 2f, min.Y), Col(0.30f, 0.62f, 0.95f), 2f);
    }

    // Resolve a click on the bottom tab bar. Returns true when the click was inside the bar (so the
    // caller stops — the strip swallows its own clicks even on empty space).
    private bool TryHandleTabBarClick(Vector2 mouse)
    {
        if (mouse.Y < LanesBottom) return false;
        if (!IG.IsMouseClicked(ImGuiMouseButton.Left)) return true;
        // A clip tab: ✕ closes it, the body shows that clip's page.
        foreach (var t in _tabHits)
        {
            if (InRect(mouse, t.closeMin, t.closeMax)) { CloseTab(t.id); return true; }
            if (InRect(mouse, t.min, t.max)) { _activeClipId = t.id; _mode = EditorMode.Clip; return true; }
        }
        if (InRect(mouse, _tlTabMin, _tlTabMax)) _mode = EditorMode.Timeline;
        return true;
    }

    // Open (or focus) a clip's keyframe tab. Audio clips have no keyframes yet, so they never get a tab.
    private void OpenClipTab(ClipData clip)
    {
        if (clip.IsAudio) return;
        if (!_openTabs.Contains(clip.Id)) _openTabs.Add(clip.Id);
    }

    // Open (or re-focus) a clip's keyframe page: select the clip, ensure its bottom tab exists, and
    // switch the canvas to the clip view. The single entry point for every gesture that opens the
    // page (double-clicking a clip body, the inspector's "Edit Keyframes" button). Audio clips have
    // no keyframes yet, so it's a no-op for them.
    private void OpenClipKeyframes(ClipData clip)
    {
        if (clip.IsAudio) return;
        SelectSingle(clip.Id);
        OpenClipTab(clip);
        _activeClipId = clip.Id;
        _mode = EditorMode.Clip;
    }

    // Remove a clip's tab; if it was the one on screen, drop back to the timeline.
    private void CloseTab(string id)
    {
        _openTabs.Remove(id);
        if (_activeClipId == id) { _activeClipId = null; _mode = EditorMode.Timeline; }
    }

    // ---- clip (keyframe) page: float tracks on the shared timeline axis ----
    //
    // The clip page reuses the timeline's ruler / loop / marker / playhead / transport unchanged (same
    // _pxPerFrame / _scrollX), so you see the exact same moment in time — only the header column and
    // lane content are swapped for the open clip's float tracks. These three methods are the
    // float-track twins of DrawLanes / DrawHeaders / DrawCorner; colours and metrics are copied so the
    // views stay identical. Rows stack from _lanesTop at TrackHeight each (no vertical scroll yet).

    // Lane area: one row per float track, with the shared time grid and a faint band showing where the
    // clip sits on the timeline. Keyframes will be drawn here later (the timeline's clips → keyframes).
    private void DrawFloatLanes(ImDrawListPtr dl)
    {
        var found = FindClip(_activeClipId);
        if (found is null) return;
        var (_, clip) = found.Value;
        var (cr, cg, cb) = ClipRgb(clip.Color);
        float cx0 = FrameToX(clip.Start), cx1 = FrameToX(clip.End);

        for (int i = 0; i < clip.FloatTracks.Count; i++)
        {
            float top = _lanesTop + i * TrackHeight;
            if (top >= LanesBottom) break;
            float rowBot = top + TrackHeight, bot = MathF.Min(rowBot, LanesBottom);
            uint bg = (i % 2 == 0) ? Col(0.135f, 0.145f, 0.155f) : Col(0.115f, 0.125f, 0.135f);
            dl.AddRectFilled(new Vector2(_laneLeft, top), new Vector2(LaneRight, bot), bg);
            // clip-extent band (faint tint in the clip's colour) so its span reads on the axis
            float bx0 = MathF.Max(cx0, _laneLeft), bx1 = MathF.Min(cx1, LaneRight);
            if (bx1 > bx0) dl.AddRectFilled(new Vector2(bx0, top + 1f), new Vector2(bx1, bot - 1f), Col(cr, cg, cb, 0.10f));
            if (rowBot <= LanesBottom)
                dl.AddLine(new Vector2(_laneLeft, rowBot), new Vector2(LaneRight, rowBot), Col(0f, 0f, 0f, 0.35f));
        }

        DrawTimeGrid(dl);   // same vertical grid as the timeline

        // Darken the timeline outside the clip's span (before its start / after its end) so the clip's
        // active region stands out. Drawn over the rows + grid; the clip edges below stay crisp.
        uint shade = Col(0f, 0f, 0f, 0.38f);
        if (cx0 > _laneLeft) dl.AddRectFilled(new Vector2(_laneLeft, _lanesTop), new Vector2(MathF.Min(cx0, LaneRight), LanesBottom), shade);
        if (cx1 < LaneRight) dl.AddRectFilled(new Vector2(MathF.Max(cx1, _laneLeft), _lanesTop), new Vector2(LaneRight, LanesBottom), shade);

        // Clip start / end as thin lines in the clip colour, full lane height.
        if (cx0 >= _laneLeft && cx0 <= LaneRight) dl.AddLine(new Vector2(cx0, _lanesTop), new Vector2(cx0, LanesBottom), Col(cr, cg, cb, 0.55f), 1.5f);
        if (cx1 >= _laneLeft && cx1 <= LaneRight) dl.AddLine(new Vector2(cx1, _lanesTop), new Vector2(cx1, LanesBottom), Col(cr, cg, cb, 0.55f), 1.5f);

        // Keyframe diamonds, one per key, centred in the track's row (keyframe frame is clip-local).
        for (int i = 0; i < clip.FloatTracks.Count; i++)
        {
            float top = _lanesTop + i * TrackHeight;
            if (top >= LanesBottom) break;
            if (top + TrackHeight > LanesBottom) continue;   // skip a partial last row
            float rcy = top + TrackHeight * 0.5f;
            foreach (var kf in clip.FloatTracks[i].Keys)
            {
                float kx = FrameToX(clip.Start + kf.Frame);
                if (kx < _laneLeft - 7f || kx > LaneRight + 7f) continue;
                DrawKeyframeMarker(dl, kx, rcy, kf.Interp, clip.Start + kf.Frame == Document.Playhead, _kfSelection.Contains(kf.Id));
            }
        }
    }

    // Draw a keyframe marker whose SHAPE encodes its interpolation type (industry-standard icons):
    // Linear = diamond, Step = square, EaseIn = left arrow, EaseOut = right arrow, EasyEase = hourglass,
    // AutoBezier = circle. Fill is amber (brighter when on the playhead / selected); selected keys get a
    // bright blue outline, others a thin dark one — both following the shape.
    private void DrawKeyframeMarker(ImDrawListPtr dl, float x, float y, InterpMode interp, bool onPlayhead, bool selected)
    {
        float r = (onPlayhead || selected) ? 9.5f : 8f;
        uint fill = selected ? Col(1f, 0.98f, 0.85f) : onPlayhead ? Col(1f, 0.9f, 0.45f) : Col(0.95f, 0.8f, 0.3f);
        uint ring = selected ? Col(0.35f, 0.7f, 1f) : Col(0.12f, 0.10f, 0.04f);
        float rw = selected ? 2f : 1.2f;

        switch (interp)
        {
            case InterpMode.Step:   // square
            {
                float s = r * 0.82f;
                var mn = new Vector2(x - s, y - s); var mx = new Vector2(x + s, y + s);
                dl.AddRectFilled(mn, mx, fill);
                dl.AddRect(mn, mx, ring, 0f, ImDrawFlags.None, rw);
                break;
            }
            case InterpMode.AutoBezier:   // circle
            {
                float cr = r * 0.92f;
                dl.AddCircleFilled(new Vector2(x, y), cr, fill, 18);
                dl.AddCircle(new Vector2(x, y), cr, ring, 18, rw);
                break;
            }
            case InterpMode.EaseIn:   // left-facing arrow (the eased side is the incoming / left side)
            {
                var p0 = new Vector2(x - r, y);
                var p1 = new Vector2(x + r * 0.7f, y - r);
                var p2 = new Vector2(x + r * 0.7f, y + r);
                dl.AddTriangleFilled(p0, p1, p2, fill);
                dl.AddTriangle(p0, p1, p2, ring, rw);
                break;
            }
            case InterpMode.EaseOut:   // right-facing arrow (eased outgoing / right side)
            {
                var p0 = new Vector2(x + r, y);
                var p1 = new Vector2(x - r * 0.7f, y - r);
                var p2 = new Vector2(x - r * 0.7f, y + r);
                dl.AddTriangleFilled(p0, p1, p2, fill);
                dl.AddTriangle(p0, p1, p2, ring, rw);
                break;
            }
            case InterpMode.EasyEase:   // hourglass (two triangles meeting tip-to-tip)
            {
                var tl = new Vector2(x - r, y - r); var tr = new Vector2(x + r, y - r);
                var bl = new Vector2(x - r, y + r); var br = new Vector2(x + r, y + r);
                var c = new Vector2(x, y);
                dl.AddTriangleFilled(tl, tr, c, fill);
                dl.AddTriangleFilled(bl, br, c, fill);
                dl.AddTriangle(tl, tr, c, ring, rw);
                dl.AddTriangle(bl, br, c, ring, rw);
                break;
            }
            default:   // Linear = diamond
            {
                var t = new Vector2(x, y - r); var rt = new Vector2(x + r, y);
                var bt = new Vector2(x, y + r); var l = new Vector2(x - r, y);
                dl.AddQuadFilled(t, rt, bt, l, fill);
                dl.AddQuad(t, rt, bt, l, ring, rw);
                break;
            }
        }
    }

    // ---- keyframe interpolation context menu ----

    private void OpenKeyframeMenu(Vector2 at)
    {
        _kfMenuRequestOpen = true;
        _kfMenuOpen = true;
        _kfMenuPos = at;
    }

    // The interpolation type shared by every selected keyframe, or null when the selection is empty or
    // mixed — drives the check mark in the menu.
    private InterpMode? CommonSelectedInterp()
    {
        if (FindClip(_activeClipId) is not { } f) return null;
        InterpMode? common = null;
        foreach (var tr in f.clip.FloatTracks)
            foreach (var k in tr.Keys)
                if (_kfSelection.Contains(k.Id))
                {
                    if (common is null) common = k.Interp;
                    else if (common != k.Interp) return null;
                }
        return common;
    }

    // Set the interpolation type on every selected keyframe, as one undo step.
    private void ApplyInterpToSelection(InterpMode mode)
    {
        if (FindClip(_activeClipId) is not { } f) return;
        bool any = false;
        foreach (var tr in f.clip.FloatTracks)
            foreach (var k in tr.Keys)
                if (_kfSelection.Contains(k.Id)) { k.Interp = mode; any = true; }
        if (any) Commit();
    }

    // The right-click keyframe menu: the six interpolation types, applied to the selection. A plain
    // (non-modal) popup — clicking a type or clicking away closes it. _kfMenuOpen gates the editor's
    // own interaction/shortcuts while it's up.
    private void DrawKeyframeMenu()
    {
        const string id = "kf-interp##interpole";
        if (_kfMenuRequestOpen) { IG.SetNextWindowPos(_kfMenuPos, ImGuiCond.Appearing); IG.OpenPopup(id); _kfMenuRequestOpen = false; }
        if (!_kfMenuOpen) return;

        if (IG.BeginPopup(id))
        {
            InterpMode? cur = CommonSelectedInterp();
            IG.TextDisabled("Interpolation");
            IG.Separator();
            KfMenuItem("Linear", InterpMode.Linear, cur);
            KfMenuItem("Step", InterpMode.Step, cur);
            KfMenuItem("Ease In", InterpMode.EaseIn, cur);
            KfMenuItem("Ease Out", InterpMode.EaseOut, cur);
            KfMenuItem("Easy Ease", InterpMode.EasyEase, cur);
            KfMenuItem("Auto Bezier", InterpMode.AutoBezier, cur);
            IG.EndPopup();
        }
        else _kfMenuOpen = false;   // clicked a type or clicked away
    }

    private void KfMenuItem(string label, InterpMode mode, InterpMode? current)
    {
        if (IG.MenuItem(label, "", current == mode)) ApplyInterpToSelection(mode);
    }

    // Header column: name + draggable value per float track, plus the timeline's row styling (accent
    // bar + separators). Delete a track the timeline way — Alt+Left / Middle click on its header cell.
    // The top of float-track row i on the clip page. Rows are uniform TrackHeight from _lanesTop.
    private float FloatRowTop(int i) => _lanesTop + i * TrackHeight;

    // The float-track index under a Y coordinate in the header/lane area, or -1 if none. Uniform rows.
    private int FloatTrackIndexAtY(ClipData clip, float y)
    {
        if (y < _lanesTop) return -1;
        int i = (int)((y - _lanesTop) / TrackHeight);
        return (i >= 0 && i < clip.FloatTracks.Count) ? i : -1;
    }

    // The name column's width in a float-track header — the row width minus the fixed value field.
    // Shared by the header draw and the inline rename box so the two line up.
    private float FloatHeaderNameWidth() => MathF.Max(60f, (_laneLeft - 12f) - (_origin.X + 12f) - 96f - 8f);

    private void DrawFloatHeaders(ImDrawListPtr dl)
    {
        var found = FindClip(_activeClipId);
        if (found is null) return;
        var (_, clip) = found.Value;

        dl.AddRectFilled(new Vector2(_origin.X, _lanesTop), new Vector2(_laneLeft, LanesBottom), Col(0.15f, 0.16f, 0.18f));

        for (int i = 0; i < clip.FloatTracks.Count; i++)
        {
            float top = FloatRowTop(i);
            if (top >= LanesBottom) break;
            float rowBot = top + TrackHeight, bot = MathF.Min(rowBot, LanesBottom);
            var ft = clip.FloatTracks[i];

            uint bg = (i % 2 == 0) ? Col(0.17f, 0.18f, 0.20f) : Col(0.15f, 0.16f, 0.18f);
            dl.AddRectFilled(new Vector2(_origin.X, top), new Vector2(_laneLeft, bot), bg);
            dl.AddRectFilled(new Vector2(_origin.X, top), new Vector2(_origin.X + 5f, bot), Col(0.25f, 0.55f, 0.85f));
            if (rowBot <= LanesBottom)
                dl.AddLine(new Vector2(_origin.X, rowBot), new Vector2(_laneLeft, rowBot), Col(0f, 0f, 0f, 0.35f));

            // Name + value, only when the row fully fits (keeps them off the tab bar).
            if (rowBot <= LanesBottom)
            {
                const float valueW = 96f, gap = 8f;
                float nameW = FloatHeaderNameWidth();
                // Name is plain text (like the timeline's track headers): double-click to rename, drag to
                // reorder, Alt/Middle-click to delete — all handled in HandleClipLaneInteraction. During
                // an inline rename the edit box (opaque) is drawn over it.
                string name = string.IsNullOrWhiteSpace(ft.Name) ? ft.Id : ft.Name;
                dl.PushClipRect(new Vector2(_origin.X + 12f, top), new Vector2(_origin.X + 12f + nameW, bot), true);
                var ts = IG.CalcTextSize(name);
                dl.AddText(new Vector2(_origin.X + 12f, (top + rowBot) * 0.5f - ts.Y * 0.5f), Col(0.9f, 0.94f, 1f), name);
                dl.PopClipRect();

                // Value AT THE PLAYHEAD (After-Effects style) — the only live widget in the header. The
                // field shows the sampled value, and editing it creates a keyframe at the playhead (or
                // updates the one there). Only inside the clip's span; amber when the playhead is on a key.
                IG.PushID(ft.Id);
                float wy = top + (TrackHeight - IG.GetFrameHeight()) * 0.5f;   // vertical centre
                int local = Document.Playhead - clip.Start;
                bool within = local >= 0 && local < clip.Length;
                bool onKey = within && ft.Keys.Any(k => k.Frame == local);
                float valX = _origin.X + 12f + nameW + gap;
                IG.SetCursorScreenPos(new Vector2(valX, wy));
                IG.SetNextItemWidth(valueW);
                IG.PushStyleColor(ImGuiCol.FrameBg, onKey ? Col(0.44f, 0.35f, 0.12f) : Col(0.20f, 0.21f, 0.24f));
                if (!within) IG.BeginDisabled();
                float val = ft.SampleAt(within ? local : Math.Clamp(local, 0, clip.Length - 1));
                if (IG.DragFloat("##value", ref val, 0.01f) && within) ft.SetKeyframe(local, val);
                if (IG.IsItemDeactivatedAfterEdit() && within) Commit();
                if (!within) IG.EndDisabled();
                IG.PopStyleColor();
                IG.PopID();
            }
        }

        dl.AddLine(new Vector2(_laneLeft, _lanesTop), new Vector2(_laneLeft, LanesBottom), Col(0f, 0f, 0f, 0.5f));

        if (clip.FloatTracks.Count == 0)
        {
            const string hint = "No float tracks — click \"+ Float Track\".";
            var hs = IG.CalcTextSize(hint);
            dl.AddText(new Vector2(_laneLeft + (LaneRight - _laneLeft - hs.X) * 0.5f, _lanesTop + 18f), Col(0.5f, 0.54f, 0.6f), hint);
        }
    }

    // Corner above the header column: the "+ Float Track" button — the clip page's twin of DrawCorner.
    private void DrawFloatCorner(ImDrawListPtr dl, Vector2 mouse)
    {
        var found = FindClip(_activeClipId);
        if (found is null) return;
        var (_, clip) = found.Value;

        dl.AddRectFilled(new Vector2(_origin.X, _rulerTop), new Vector2(_laneLeft, _rulerBottom), Col(0.13f, 0.14f, 0.16f));
        // Match the timeline's + Track / + Audio corner buttons: same rect metrics (a 4px margin in the
        // ruler band → height RulerHeight-8). ImGui centres the label vertically, so it won't clip.
        var (bMin, bMax) = (new Vector2(_origin.X + 8f, _rulerTop + 4f), new Vector2(_laneLeft - 8f, _rulerBottom - 4f));
        IG.SetCursorScreenPos(bMin);
        IG.PushStyleColor(ImGuiCol.Button, Col(0.22f, 0.4f, 0.6f));
        IG.PushStyleColor(ImGuiCol.ButtonHovered, Col(0.28f, 0.5f, 0.75f));
        IG.PushStyleColor(ImGuiCol.ButtonActive, Col(0.24f, 0.44f, 0.66f));
        if (IG.Button("+ Float Track", new Vector2(bMax.X - bMin.X, bMax.Y - bMin.Y))) AddFloatTrack(clip);
        IG.PopStyleColor(3);
    }

    // Append a new float track to the clip with a default name/value, as one undo step.
    private void AddFloatTrack(ClipData clip)
    {
        clip.FloatTracks.Add(new FloatTrackData
        {
            Id = InterpoleDocument.NewId("ft"),
            Name = "Float " + (clip.FloatTracks.Count + 1),
            Value = 0f,
        });
        Commit();
    }

    // ---- keyframe selection / copy / paste (clip page lane) ----

    // Backfill ids on any keyframes that lack one (old data) and drop selected ids whose keyframe is
    // gone (deleted / undo / a different clip is now active). Runs once per frame on the clip page.
    private void EnsureKeyframeIdsAndPruneSelection()
    {
        if (FindClip(_activeClipId) is not { } f) { _kfSelection.Clear(); return; }
        var live = new HashSet<string>();
        foreach (var t in f.clip.FloatTracks)
            foreach (var k in t.Keys)
            {
                if (string.IsNullOrEmpty(k.Id)) k.Id = InterpoleDocument.NewId("kf");
                live.Add(k.Id);
            }
        _kfSelection.RemoveWhere(id => !live.Contains(id));
    }

    // The keyframe under the cursor in the active clip's lane (with its track index), or null.
    private (int trackIndex, Keyframe kf)? HitKeyframe(Vector2 mouse)
    {
        if (FindClip(_activeClipId) is not { } f) return null;
        if (mouse.X < _laneLeft || mouse.X >= LaneRight || mouse.Y < _lanesTop || mouse.Y >= LanesBottom) return null;
        var clip = f.clip;
        const float r = 10f;
        for (int i = 0; i < clip.FloatTracks.Count; i++)
        {
            float top = _lanesTop + i * TrackHeight;
            if (top >= LanesBottom) break;
            if (top + TrackHeight > LanesBottom) continue;
            float rcy = top + TrackHeight * 0.5f;
            if (MathF.Abs(mouse.Y - rcy) > r + 3f) continue;
            foreach (var kf in clip.FloatTracks[i].Keys)
                if (MathF.Abs(mouse.X - FrameToX(clip.Start + kf.Frame)) <= r) return (i, kf);
        }
        return null;
    }

    // Lane interaction on the clip page: click a keyframe to select it (Ctrl toggles / adds),
    // Alt/Middle-click to delete it, or drag on empty lane to marquee-select. (No keyframe drag yet.)
    private void HandleClipLaneInteraction(Vector2 mouse, ImGuiIOPtr io)
    {
        // Track-header column — mirror the timeline's track headers: double-click a name to rename,
        // drag to reorder, Alt/Middle-click to delete. Only the plain-text name region reaches here
        // (the value DragFloat sits on top and takes its own input, so !hovered returns earlier).
        if (mouse.X < _laneLeft && mouse.Y >= _lanesTop && mouse.Y < LanesBottom)
        {
            if (FindClip(_activeClipId) is not { } fc) return;
            int idx = FloatTrackIndexAtY(fc.clip, mouse.Y);
            if (idx >= 0)
            {
                var ft = fc.clip.FloatTracks[idx];
                if (IG.IsMouseDoubleClicked(ImGuiMouseButton.Left)) StartEditFloatTrackName(ft);
                else if (IG.IsMouseClicked(ImGuiMouseButton.Middle) || (io.KeyAlt && IG.IsMouseClicked(ImGuiMouseButton.Left)))
                { fc.clip.FloatTracks.RemoveAll(t => t.Id == ft.Id); Commit(); }
                else if (IG.IsMouseClicked(ImGuiMouseButton.Left))   // plain click-drag reorders
                { _drag = DragMode.MoveFloatTrack; _dragTrackId = ft.Id; }
            }
            return;
        }
        if (mouse.X < _laneLeft || mouse.X >= LaneRight || mouse.Y < _lanesTop || mouse.Y >= LanesBottom) return;
        if (HitKeyframe(mouse) is { } h)
        {
            if (IG.IsMouseClicked(ImGuiMouseButton.Middle) || (io.KeyAlt && IG.IsMouseClicked(ImGuiMouseButton.Left)))
            { DeleteKeyframe(h.trackIndex, h.kf); return; }
            if (IG.IsMouseClicked(ImGuiMouseButton.Left))
            {
                // Ctrl-click toggles this keyframe in the selection (no drag).
                if (io.KeyCtrl) { if (!_kfSelection.Add(h.kf.Id)) _kfSelection.Remove(h.kf.Id); return; }
                // Grabbing a keyframe already in a multi-selection keeps the group so they drag
                // together; grabbing any other selects just it. A click with no movement = select.
                if (!_kfSelection.Contains(h.kf.Id)) { _kfSelection.Clear(); _kfSelection.Add(h.kf.Id); }
                BeginKeyframeMove(h.kf, mouse);
                _drag = DragMode.MoveKeyframe;
            }
            return;
        }
        // Empty lane → marquee (a plain click with no drag ends up clearing the selection).
        if (IG.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _drag = DragMode.KfMarquee;
            _marqueeAnchor = mouse;
            _marqueeBase = (io.KeyShift || io.KeyCtrl) ? new HashSet<string>(_kfSelection) : new HashSet<string>();
            UpdateKfMarqueeSelection(mouse);
        }
    }

    private void ContinueKfMarquee(Vector2 mouse)
    {
        if (!IG.IsMouseDown(ImGuiMouseButton.Left)) { _drag = DragMode.None; return; }
        UpdateKfMarqueeSelection(mouse);
    }

    // Selection = the additive base plus every keyframe whose diamond falls inside the band.
    private void UpdateKfMarqueeSelection(Vector2 mouse)
    {
        if (FindClip(_activeClipId) is not { } f) return;
        var (min, max) = MarqueeRect(mouse);
        _kfSelection.Clear();
        _kfSelection.UnionWith(_marqueeBase);
        var clip = f.clip;
        for (int i = 0; i < clip.FloatTracks.Count; i++)
        {
            float top = _lanesTop + i * TrackHeight;
            if (top >= LanesBottom) break;
            if (top + TrackHeight > LanesBottom) continue;
            float rcy = top + TrackHeight * 0.5f;
            if (rcy < min.Y || rcy > max.Y) continue;
            foreach (var kf in clip.FloatTracks[i].Keys)
                if (FrameToX(clip.Start + kf.Frame) is var kx && kx >= min.X && kx <= max.X) _kfSelection.Add(kf.Id);
        }
    }

    private void DeleteKeyframe(int trackIndex, Keyframe kf)
    {
        if (FindClip(_activeClipId) is not { } f) return;
        f.clip.FloatTracks[trackIndex].Keys.RemoveAll(k => ReferenceEquals(k, kf));
        _kfSelection.Remove(kf.Id);
        Commit();
    }

    // Delete every selected keyframe across the active clip's tracks, in one undo step.
    private void DeleteSelectedKeyframes()
    {
        if (_kfSelection.Count == 0 || FindClip(_activeClipId) is not { } f) return;
        foreach (var t in f.clip.FloatTracks) t.Keys.RemoveAll(k => _kfSelection.Contains(k.Id));
        _kfSelection.Clear();
        Commit();
    }

    // Copy the selected keyframes (per track index, with their clip-local frames + values).
    private void CopyKeyframes()
    {
        _kfClipboard.Clear();
        if (_kfSelection.Count == 0 || FindClip(_activeClipId) is not { } f) return;
        for (int i = 0; i < f.clip.FloatTracks.Count; i++)
            foreach (var k in f.clip.FloatTracks[i].Keys)
                if (_kfSelection.Contains(k.Id)) _kfClipboard.Add((i, k.Frame, k.Value));
    }

    // Paste copied keyframes at the playhead: the earliest copied keyframe lands on the playhead, the
    // rest keep their relative offsets, into the track at the same index. The pasted keys are selected.
    private void PasteKeyframes()
    {
        if (_kfClipboard.Count == 0 || FindClip(_activeClipId) is not { } f) return;
        var clip = f.clip;
        int baseLocal = Math.Max(0, Document.Playhead - clip.Start);
        int minFrame = _kfClipboard.Min(c => c.frame);
        _kfSelection.Clear();
        foreach (var c in _kfClipboard)
        {
            if (c.trackIndex >= clip.FloatTracks.Count) continue;   // no matching track in this clip
            int newFrame = baseLocal + (c.frame - minFrame);
            if (newFrame < 0) continue;
            _kfSelection.Add(clip.FloatTracks[c.trackIndex].SetKeyframe(newFrame, c.value).Id);
        }
        Commit();
    }

    // Capture the frames of the selected keyframes at grab time so the group can move by a delta.
    private void BeginKeyframeMove(Keyframe grabbed, Vector2 mouse)
    {
        _kfDragOrig.Clear();
        if (FindClip(_activeClipId) is not { } f) return;
        foreach (var t in f.clip.FloatTracks)
            foreach (var k in t.Keys)
                if (_kfSelection.Contains(k.Id)) _kfDragOrig.Add((k, k.Frame));
        _kfDragGrabbedOrig = grabbed.Frame;
        _kfDragGrabMouseFrame = XToFrame(mouse.X);
        _kfDragMinOrig = _kfDragOrig.Count > 0 ? _kfDragOrig.Min(o => o.orig) : 0;
    }

    // Move the selected keyframes along time by the mouse delta. Shift snaps the grabbed keyframe to
    // clip edges / the playhead / other keyframes; the group is lower-clamped so none goes before the
    // clip start. Commits one undo step on release.
    private void ContinueKeyframeDrag(Vector2 mouse)
    {
        if (!IG.IsMouseDown(ImGuiMouseButton.Left)) { _drag = DragMode.None; _clipSnapActive = false; Commit(); return; }
        if (FindClip(_activeClipId) is not { } f) { _drag = DragMode.None; _clipSnapActive = false; return; }
        var clip = f.clip;
        int delta = (int)MathF.Round(XToFrame(mouse.X) - _kfDragGrabMouseFrame);
        _clipSnapActive = false;
        if (IG.GetIO().KeyShift)
        {
            CollectClipEdgeTargets(null);
            _snapTargets.Add(Document.Playhead);
            AddOtherKeyframeSnapTargets();
            int grabbedAbs = clip.Start + _kfDragGrabbedOrig + delta;
            var s = Snap(grabbedAbs);
            if (s.snapped) { delta += s.frame - grabbedAbs; _clipSnapActive = true; _clipSnapFrame = s.frame; }
        }
        if (_kfDragMinOrig + delta < 0) delta = -_kfDragMinOrig;   // keep the earliest key >= clip start
        foreach (var (kf, orig) in _kfDragOrig) kf.Frame = orig + delta;
        foreach (var t in clip.FloatTracks) t.Keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
    }

    // Active-clip keyframes NOT in the current selection — snap targets for a keyframe move.
    private void AddOtherKeyframeSnapTargets()
    {
        if (FindClip(_activeClipId) is not { } f) return;
        foreach (var t in f.clip.FloatTracks)
            foreach (var k in t.Keys)
                if (!_kfSelection.Contains(k.Id)) _snapTargets.Add(f.clip.Start + k.Frame);
    }

    // ============================== interaction ==============================

    private bool InLanes(Vector2 m) => m.X >= _laneLeft && m.X < LaneRight && m.Y >= _lanesTop;
    private static bool InRect(Vector2 p, Vector2 min, Vector2 max)
        => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;

    // Ctrl+Z undo, Ctrl+Y / Ctrl+Shift+Z redo, Ctrl+C copy clip, Ctrl+V paste at playhead. Only
    // when no inline edit is open (there the InputText owns the keyboard).
    // Play-mode keyboard: only Space (stop). No edit shortcuts — the timeline plays, it doesn't edit.
    private void HandlePlayShortcuts(ImGuiIOPtr io)
    {
        if (io.WantTextInput) return;
        if (IG.IsKeyPressed(ImGuiKey.Space, false)) OnTransport(_playing ? TButton.Stop : TButton.Play);
    }

    // Play-mode mouse: only the things that aren't document edits — continue/begin a playhead scrub,
    // zoom the time axis, and click the transport bar (Play/Stop/Rewind/Loop + time toggle). Everything
    // else (clip/track/marker/keyframe edits, inspector, loop-ribbon edits, pan, tab bar) is blocked.
    private void HandlePlayInteraction(bool hovered, Vector2 mouse, ImGuiIOPtr io)
    {
        // Continue a playhead scrub already in progress; drop any other stray drag/pan.
        if (_drag == DragMode.Playhead) { ContinuePlayheadDrag(mouse); return; }
        if (_drag != DragMode.None) { _drag = DragMode.None; }
        _panning = false;

        if (!hovered) return;

        // Zoom the time axis around the cursor (over the lanes / ruler). Changes _pxPerFrame → the node
        // sees the view change and re-captures the cache at the new zoom.
        if (io.MouseWheel != 0f && mouse.X >= _laneLeft)
        {
            float old = _pxPerFrame;
            _pxPerFrame = Math.Clamp(_pxPerFrame * MathF.Pow(1.1f, io.MouseWheel), MinPxPerFrame, MaxPxPerFrame);
            float frameUnder = (mouse.X - _laneLeft + _scrollX) / old;
            _scrollX = frameUnder * _pxPerFrame - (mouse.X - _laneLeft);
            if (_scrollX < 0) _scrollX = 0;
        }

        // Transport bar (topmost strip): buttons + time/frames toggle. Same behaviour as full mode.
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
                        if (InRect(mouse, bmin, bmax))
                        {
                            var b = (TButton)i;
                            OnTransport(b == TButton.Play && _playing ? TButton.Stop : b);
                            break;
                        }
                    }
            }
            return;
        }

        // Ruler strip (below the loop strip, above the marker lane): click / drag to scrub the playhead.
        if (mouse.X >= _laneLeft && mouse.Y >= _loopBottom && mouse.Y < _rulerBottom)
        {
            if (IG.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _drag = DragMode.Playhead;
                Document.Playhead = PlayheadFrameFromMouse(mouse.X, io.KeyShift, out _playheadSnapped);
            }
        }
    }

    private void HandleShortcuts(ImGuiIOPtr io)
    {
        if (io.WantTextInput) return;   // an ImGui field (e.g. an inspector input) owns the keyboard

        // Escape leaves the keyframe page and drops back to the timeline (the clip's tab stays open).
        if (_mode == EditorMode.Clip && IG.IsKeyPressed(ImGuiKey.Escape, false)) { _mode = EditorMode.Timeline; return; }

        // Space toggles play / stop. B / N set the loop in / out to the playhead. Undo/redo apply in
        // both views (float-track edits are undoable too). These are all axis/transport-level, so they
        // work identically on the clip page.
        if (IG.IsKeyPressed(ImGuiKey.Space, false)) OnTransport(_playing ? TButton.Stop : TButton.Play);
        if (IG.IsKeyPressed(ImGuiKey.B, false)) SetLoopInToPlayhead();
        if (IG.IsKeyPressed(ImGuiKey.N, false)) SetLoopOutToPlayhead();

        // Delete / copy / paste act on clips in the timeline, and on keyframes on the clip page.
        if (IG.IsKeyPressed(ImGuiKey.Delete, false) || IG.IsKeyPressed(ImGuiKey.Backspace, false))
        { if (_mode == EditorMode.Timeline) DeleteSelection(); else DeleteSelectedKeyframes(); }

        if (!io.KeyCtrl) return;
        if (IG.IsKeyPressed(ImGuiKey.Z, false)) { if (io.KeyShift) Redo(); else Undo(); }
        else if (IG.IsKeyPressed(ImGuiKey.Y, false)) Redo();
        else if (IG.IsKeyPressed(ImGuiKey.C, false)) { if (_mode == EditorMode.Timeline) CopySelection(); else CopyKeyframes(); }
        else if (IG.IsKeyPressed(ImGuiKey.V, false)) { if (_mode == EditorMode.Timeline) PasteClipboard(); else PasteKeyframes(); }
    }

    private void HandleInteraction(bool hovered, Vector2 mouse, ImGuiIOPtr io)
    {
        // Keep the edit cursor visible for the whole gesture: resize/fade show a horizontal
        // double-arrow, a whole-clip move shows the four-way move cursor.
        switch (_drag)
        {
            case DragMode.ResizeL: case DragMode.ResizeR: IG.SetMouseCursor(ImGuiMouseCursor.ResizeEW); break;
            case DragMode.FadeIn: case DragMode.FadeOut: IG.SetMouseCursor(ImGuiMouseCursor.Hand); break;
            case DragMode.MoveClip: IG.SetMouseCursor(ImGuiMouseCursor.ResizeAll); break;
            case DragMode.MoveKeyframe: IG.SetMouseCursor(ImGuiMouseCursor.ResizeEW); break;
        }

        // Continue an in-progress drag / pan first.
        if (_drag == DragMode.Playhead) { ContinuePlayheadDrag(mouse); return; }
        if (_drag == DragMode.Marquee) { ContinueMarquee(mouse); return; }
        if (_drag is DragMode.LoopIn or DragMode.LoopOut or DragMode.LoopMove) { ContinueLoopDrag(mouse); return; }
        if (_drag == DragMode.MoveMarker) { ContinueMarkerDrag(mouse); return; }
        if (_drag == DragMode.MoveTrack) { ContinueTrackDrag(mouse); return; }
        if (_drag == DragMode.MoveFloatTrack) { ContinueFloatTrackDrag(mouse); return; }
        if (_drag == DragMode.KfMarquee) { ContinueKfMarquee(mouse); return; }
        if (_drag == DragMode.MoveKeyframe) { ContinueKeyframeDrag(mouse); return; }
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

        // Bottom tab bar (full-width strip at the very bottom): claim it before any timeline region.
        if (mouse.Y >= LanesBottom) { TryHandleTabBarClick(mouse); return; }

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

        // Right-click a keyframe (clip page) opens its interpolation menu instead of panning. Applies
        // the selection rule: a key already in the selection keeps the whole selection (menu edits them
        // all); any other becomes the sole selection first. Right-clicking empty space still pans.
        if (_mode == EditorMode.Clip && IG.IsMouseClicked(ImGuiMouseButton.Right) && HitKeyframe(mouse) is { } hk)
        {
            if (!_kfSelection.Contains(hk.kf.Id)) { _kfSelection.Clear(); _kfSelection.Add(hk.kf.Id); }
            OpenKeyframeMenu(mouse);
            return;
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
                        if (InRect(mouse, bmin, bmax))
                        {
                            // Play is a toggle: pressing it while playing stops (users expect the
                            // on-screen play control to double as pause/stop), same as the Space key.
                            var b = (TButton)i;
                            OnTransport(b == TButton.Play && _playing ? TButton.Stop : b);
                            break;
                        }
                    }
            }
            return;
        }

        // Loop ribbon (top strip of the ruler): drag an end grip, or drag the body to move the region.
        // The range is only editable via its handles (or the B/N shortcuts) — a plain click on empty
        // space does nothing (no fresh-draw), so the loop can't be accidentally collapsed to the cursor.
        // Alt/Middle-click clears it (back to the whole-content default). Checked before the ruler
        // scrub so the strip claims its clicks.
        if (mouse.X >= _laneLeft && mouse.Y >= _rulerTop && mouse.Y < _loopBottom)
        {
            if (IG.IsMouseClicked(ImGuiMouseButton.Middle) || (io.KeyAlt && IG.IsMouseClicked(ImGuiMouseButton.Left)))
            {
                if (Document.LoopIn != 0 || Document.LoopOut != 0) { Document.LoopIn = 0; Document.LoopOut = 0; Changed?.Invoke(); }
            }
            else if (IG.IsMouseClicked(ImGuiMouseButton.Left))
            {
                var (li, lo) = Document.EffectiveLoop();
                float xi = FrameToX(li), xo = FrameToX(lo);
                float mf = XToFrame(mouse.X);
                if (lo > li && MathF.Abs(mouse.X - xi) <= EdgeGrabPx) { MaterializeLoop(li, lo); _drag = DragMode.LoopIn; }
                else if (lo > li && MathF.Abs(mouse.X - xo) <= EdgeGrabPx) { MaterializeLoop(li, lo); _drag = DragMode.LoopOut; }
                else if (lo > li && mouse.X > xi && mouse.X < xo) { MaterializeLoop(li, lo); _drag = DragMode.LoopMove; _loopGrabFrame = mf - li; }
                // Empty space: intentionally do nothing (see above) — no fresh-draw / collapse.
            }
            return;
        }

        // Ruler strip (below the loop strip, above the marker lane): click / drag to scrub the playhead.
        if (mouse.X >= _laneLeft && mouse.Y >= _loopBottom && mouse.Y < _rulerBottom)
        {
            if (IG.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _drag = DragMode.Playhead;
                Document.Playhead = PlayheadFrameFromMouse(mouse.X, io.KeyShift, out _playheadSnapped);
            }
            return;
        }

        // Marker lane (between the ruler and the track lanes): double-click empty space to create a
        // marker (opens the name/stop modal), double-click a marker to edit it, drag to move it,
        // Alt/Middle-click to delete. Mirrors the clip gestures.
        if (mouse.X >= _laneLeft && mouse.Y >= _rulerBottom && mouse.Y < _lanesTop)
        {
            var hitM = HitMarker(mouse);
            if (hitM is not null)
            {
                if (IG.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { OpenEditMarkerDialog(hitM); return; }
                if (IG.IsMouseClicked(ImGuiMouseButton.Middle) || (io.KeyAlt && IG.IsMouseClicked(ImGuiMouseButton.Left)))
                { RemoveMarker(hitM); return; }
                if (IG.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    SelectMarker(hitM.Id);   // show it in the inspector
                    _drag = DragMode.MoveMarker;
                    _dragMarkerId = hitM.Id;
                    _dragGrabFrame = XToFrame(mouse.X) - hitM.Frame;
                }
            }
            else if (IG.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                OpenNewMarkerDialog(XToFrame(mouse.X));
            }
            return;
        }

        // The clip (keyframe) page shares all the chrome above. Its header widgets and add button are
        // ImGui items handled in the DrawFloat* methods; its LANE handles keyframe selection here. The
        // timeline's corner / track-header / clip-lane branches below never run in clip mode.
        if (_mode == EditorMode.Clip) { HandleClipLaneInteraction(mouse, io); return; }

        bool alt = io.KeyAlt;

        // Corner "add track" buttons — a standard track and an audio track.
        if (InRect(mouse, AddTrackButtonRect().min, AddTrackButtonRect().max))
        {
            if (IG.IsMouseClicked(ImGuiMouseButton.Left)) AddTrack(TrackKind.Standard);
            return;
        }
        if (InRect(mouse, AddAudioTrackButtonRect().min, AddAudioTrackButtonRect().max))
        {
            if (IG.IsMouseClicked(ImGuiMouseButton.Left)) AddTrack(TrackKind.Audio);
            return;
        }

        // Track-header column.
        if (mouse.X < _laneLeft && mouse.Y >= _lanesTop)
        {
            var tr = TrackAtY(mouse.Y);
            if (tr is not null)
            {
                // Mute / Solo buttons take a plain left click first (audio tracks only).
                if (tr.Kind == TrackKind.Audio && !alt && IG.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    var (mMin, mMax, sMin, sMax) = TrackToggleRects(Document.Tracks.IndexOf(tr));
                    if (InRect(mouse, mMin, mMax)) { tr.Mute = !tr.Mute; Commit(); return; }
                    if (InRect(mouse, sMin, sMax)) { tr.Solo = !tr.Solo; Commit(); return; }
                }
                if (IG.IsMouseDoubleClicked(ImGuiMouseButton.Left)) StartEditTrackName(tr);
                else if (IG.IsMouseClicked(ImGuiMouseButton.Middle) || (alt && IG.IsMouseClicked(ImGuiMouseButton.Left)))
                    RemoveTrack(tr.Id);
                else if (IG.IsMouseClicked(ImGuiMouseButton.Left))   // plain click-drag reorders the track
                    { _drag = DragMode.MoveTrack; _dragTrackId = tr.Id; }
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
                // Hover cue: the cursor previews the gesture — resize the edges, adjust a fade handle,
                // or move the whole clip — so it's clear before pressing which one you'll get.
                IG.SetMouseCursor(zone switch
                {
                    ClipZone.ResizeL or ClipZone.ResizeR => ImGuiMouseCursor.ResizeEW,
                    ClipZone.FadeIn or ClipZone.FadeOut => ImGuiMouseCursor.Hand,
                    _ => ImGuiMouseCursor.ResizeAll,
                });
                if (IG.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    // The double-click splits by where you land: on the clip's label text it edits the
                    // clip (inline rename + colour); anywhere else on the clip body it opens the clip's
                    // keyframe page. Audio clips have no keyframes yet, so they always edit.
                    if (!clip.IsAudio && !HitClipLabel(clip, mouse)) OpenClipKeyframes(clip);
                    else StartEditClipLabel(clip);
                    return;
                }
                if (IG.IsMouseClicked(ImGuiMouseButton.Middle) || (alt && IG.IsMouseClicked(ImGuiMouseButton.Left)))
                { RemoveClip(tr, clip); return; }
                if (IG.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    // Ctrl-click toggles this clip in the selection (no drag). Shift is the snap
                    // modifier for the drag (as on the playhead), so it must NOT block the grab —
                    // a Shift-press starts a normal move / resize / fade drag that snaps.
                    if (io.KeyCtrl) { ToggleSelected(clip.Id); return; }
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
                if (tr is not null)
                {
                    if (tr.Kind == TrackKind.Audio) AddAudioClip(tr, XToFrame(mouse.X));
                    else AddClip(tr, XToFrame(mouse.X));
                }
            }
            else if (IG.IsMouseClicked(ImGuiMouseButton.Left))
            {
                // Empty lane → start a marquee. Shift/Ctrl keeps the current selection to add to;
                // otherwise it replaces it. A click with no drag bands nothing, so it deselects.
                _selectedMarkerId = null;
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
        if (id is not null) _selectedMarkerId = null;   // a clip takes the inspector from a marker
    }

    // Select a marker for the inspector, clearing any clip selection (they share the one panel).
    private void SelectMarker(string id)
    {
        _selectedMarkerId = id;
        _selection.Clear();
        _selectedClipId = null;
    }

    // Add a clip to the selection if absent, else remove it; keep the primary pointing at a
    // still-selected clip.
    private void ToggleSelected(string id)
    {
        _selectedMarkerId = null;
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
            float top = TrackTop(i) + ClipPadY, bottom = TrackTop(i) + TrackHeightOf(i) - ClipPadY;
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

    // Make the current (possibly implicit whole-content) loop range explicit so a drag edits stored
    // LoopIn/LoopOut rather than the fallback.
    private void MaterializeLoop(int inFrame, int outFrame) { Document.LoopIn = inFrame; Document.LoopOut = outFrame; }

    // Drag the loop region or one of its ends. Reuses the clip snap guide (_clipSnapActive) — a loop
    // drag and a clip drag are never concurrent. Like the playhead, the loop region is transport-like
    // and pin-drivable, so edits persist via Changed but are NOT undo steps (kept out of the snapshot,
    // same as the playhead — avoids fighting the Loop In/Out pins). A zero-span scratch (click without
    // moving on empty ribbon) is discarded back to the whole-content default.
    private void ContinueLoopDrag(Vector2 mouse)
    {
        if (!IG.IsMouseDown(ImGuiMouseButton.Left))
        {
            _drag = DragMode.None;
            _clipSnapActive = false;
            if (Document.LoopOut <= Document.LoopIn) { Document.LoopIn = 0; Document.LoopOut = 0; }
            Changed?.Invoke();
            return;
        }

        bool shift = IG.GetIO().KeyShift;
        _clipSnapActive = false;
        if (shift) { CollectClipEdgeTargets(null); _snapTargets.Add(Document.Playhead); }
        float mf = XToFrame(mouse.X);

        switch (_drag)
        {
            case DragMode.LoopIn:
            {
                int f = Math.Max(0, (int)MathF.Round(mf));
                if (shift) { var s = Snap(f); if (s.snapped) { f = s.frame; _clipSnapActive = true; _clipSnapFrame = f; } }
                Document.LoopIn = Math.Clamp(f, 0, Math.Max(0, Document.LoopOut - 1));
                break;
            }
            case DragMode.LoopOut:
            {
                int f = Math.Max(0, (int)MathF.Round(mf));
                if (shift) { var s = Snap(f); if (s.snapped) { f = s.frame; _clipSnapActive = true; _clipSnapFrame = f; } }
                Document.LoopOut = Math.Max(f, Document.LoopIn + 1);
                break;
            }
            case DragMode.LoopMove:
            {
                int span = Document.LoopOut - Document.LoopIn;
                int ni = Math.Max(0, (int)MathF.Round(mf - _loopGrabFrame));
                if (shift) { var s = Snap(ni); if (s.snapped) { ni = s.frame; _clipSnapActive = true; _clipSnapFrame = ni; } }
                Document.LoopIn = ni;
                Document.LoopOut = ni + span;
                break;
            }
        }
    }

    // B: set the loop-in to the playhead, keeping the out where it is (pushed to stay after the in).
    // Materialises the effective range so it works even from the whole-content default. Like a scrub,
    // this persists (Changed) but is not an undo step.
    private void SetLoopInToPlayhead()
    {
        var (_, lo) = Document.EffectiveLoop();
        int p = Math.Max(0, Document.Playhead);
        Document.LoopIn = p;
        Document.LoopOut = Math.Max(lo, p + 1);
        Changed?.Invoke();
    }

    // N: set the loop-out to the playhead, keeping the in where it is (pulled to stay before the out).
    private void SetLoopOutToPlayhead()
    {
        var (li, _) = Document.EffectiveLoop();
        int p = Math.Max(1, Document.Playhead);   // out must be >= 1 (half-open range needs in < out)
        Document.LoopOut = p;
        Document.LoopIn = Math.Min(li, p - 1);
        Changed?.Invoke();
    }

    // Set the loop region from the node's Loop In / Loop Out input pins. Stored raw (>= 0); an
    // invalid range (out <= in) falls back to whole-content via EffectiveLoop. Persists (Changed)
    // but is not an undo step — like a scrub — since the pins may be driven/automated.
    public void SetLoopFromPins(int inFrame, int outFrame)
    {
        int i = Math.Max(0, inFrame);
        int o = Math.Max(0, outFrame);
        if (i == Document.LoopIn && o == Document.LoopOut) return;
        Document.LoopIn = i;
        Document.LoopOut = o;
        Changed?.Invoke();
    }

    // Frame under the mouse for playhead scrubbing. With Shift held (After Effects style), snap to
    // the nearest clip start/end or fade-handle frame; otherwise round to the nearest whole frame.
    private int PlayheadFrameFromMouse(float mouseX, bool snap, out bool snapped)
    {
        snapped = false;
        int frame = Math.Max(0, (int)MathF.Round(XToFrame(mouseX)));
        if (!snap) return frame;
        CollectClipEdgeTargets(null);          // snap to every clip (nothing to exclude, no playhead)
        AddActiveKeyframeSnapTargets();        // and, on the clip page, the open clip's keyframes
        var r = Snap(frame);
        snapped = r.snapped;
        return r.frame;
    }

    // On the clip (keyframe) page, add the open clip's keyframe frames (absolute) to the snap targets so
    // the playhead scrub snaps onto keyframes too.
    private void AddActiveKeyframeSnapTargets()
    {
        if (_mode != EditorMode.Clip || FindClip(_activeClipId) is not { } f) return;
        foreach (var t in f.clip.FloatTracks)
            foreach (var k in t.Keys)
                _snapTargets.Add(f.clip.Start + k.Frame);
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

    // Source-file duration in seconds for clamping audio resizes. Uses the value captured at creation;
    // for older documents (0) it back-fills from the decoded peaks once they're ready. Returns +inf
    // when unknown or non-audio, i.e. no length clamp.
    private double AudioSourceSeconds(ClipData clip)
    {
        if (!clip.IsAudio) return double.PositiveInfinity;
        if (clip.FileDurationSeconds > 0) return clip.FileDurationSeconds;
        if (clip.FilePath is not null && WaveformCache.Get(clip.FilePath, out var pk) == WaveformState.Ready
            && pk is not null && pk.DurationSeconds > 0)
        {
            clip.FileDurationSeconds = pk.DurationSeconds;   // self-heal so it persists on next save
            return pk.DurationSeconds;
        }
        return double.PositiveInfinity;
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
                // Audio: the left edge can pull the trimmed-in head back out only to the file's start
                // (FileInPoint 0), never before it — so the clip can't grow past the media at the head.
                if (clip.IsAudio) lo = Math.Max(lo, clip.Start - (int)MathF.Round((float)(clip.FileInPoint * _fps)));
                int raw = (int)MathF.Round(mf);
                if (shift) { var s = Snap(raw); if (s.snapped) { raw = s.frame; _clipSnapActive = true; _clipSnapFrame = s.frame; } }
                int ns = Math.Clamp(raw, lo, end - 1);
                if (_clipSnapActive && ns != _clipSnapFrame) _clipSnapActive = false;   // bound overrode the snap
                // Audio clips head-trim: moving the left edge advances/retreats the file in-point by
                // the same delta, so the audio content stays put on the timeline (clamped at the
                // file's head, so you can't reveal media before sample 0).
                if (clip.IsAudio) clip.FileInPoint = Math.Max(0, clip.FileInPoint + (ns - clip.Start) / (double)_fps);
                clip.Start = ns;
                clip.Length = end - ns;
                ClampFades(clip);   // shorter clip may no longer fit both fades
                break;
            }
            case DragMode.ResizeR:
            {
                int hi = RightBound(tr, clip);
                // Audio: the right edge can extend only until the file runs out (from the current
                // in-point), so the clip can't grow past the end of the media it plays.
                double dur = AudioSourceSeconds(clip);
                if (!double.IsPositiveInfinity(dur))
                    hi = Math.Min(hi, clip.Start + (int)MathF.Round((float)((dur - clip.FileInPoint) * _fps)));
                int raw = (int)MathF.Round(mf);
                if (shift) { var s = Snap(raw); if (s.snapped) { raw = s.frame; _clipSnapActive = true; _clipSnapFrame = s.frame; } }
                int ne = Math.Clamp(raw, clip.Start + 1, Math.Max(clip.Start + 1, hi));
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
            // Clips can't cross between audio and standard tracks — audio clips stay on audio tracks
            // and vice versa. A destination of a different kind blocks the whole vertical shift.
            if (Document.Tracks[ti].Kind != tr.Kind) return false;
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
        float cursor = _lanesTop - _scrollY;
        for (int i = 0; i < Document.Tracks.Count; i++)
        {
            float h = TrackHeightOf(i);
            if (y < cursor + h) return y >= cursor ? i : -1;
            cursor += h;
        }
        return -1;
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
        float y0 = top + ClipPadY, y1 = top + TrackHeightOf(i) - ClipPadY;
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

    // Whether the mouse is over a clip's drawn label — the left band spanning the label text (from
    // the clip's left edge through the end of the drawn text) across the full clip-body height. Used
    // to split the double-click gesture: on the label it edits the clip, elsewhere on the body it
    // opens the keyframe page. Same text geometry as DrawClip so the split matches what's on screen.
    private bool HitClipLabel(ClipData clip, Vector2 mouse)
    {
        int i = TrackIndexAtY(mouse.Y);
        if (i < 0) return false;
        float top = TrackTop(i);
        float y0 = top + ClipPadY, y1 = top + TrackHeightOf(i) - ClipPadY;
        float x0 = FrameToX(clip.Start);
        string lbl = string.IsNullOrWhiteSpace(clip.Label) ? clip.Id : clip.Label;
        float lblRight = x0 + 6f + IG.CalcTextSize(lbl).X + 4f;
        return mouse.X >= x0 && mouse.X <= lblRight && mouse.Y >= y0 && mouse.Y <= y1;
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

    private void AddTrack(TrackKind kind = TrackKind.Standard)
    {
        _trackCounter++;
        string name = (kind == TrackKind.Audio ? "Audio " : "Track ") + _trackCounter;
        Document.Tracks.Add(new TrackData { Id = InterpoleDocument.NewId("tr"), Name = name, Kind = kind });
        Commit();
    }

    private void RemoveTrack(string id)
    {
        Document.Tracks.RemoveAll(t => t.Id == id);
        _selection.RemoveWhere(cid => FindClip(cid) is null);   // drop clips that lived on this track
        if (FindClip(_selectedClipId) is null) _selectedClipId = _selection.FirstOrDefault();
        Commit();
    }

    // Frames for a freshly added clip. DefaultClipLen at 100% zoom (and above), but grows as you zoom
    // OUT so a new clip keeps a comfortable, grabbable on-screen width — both edge handles reachable —
    // instead of a few-pixel sliver at low zoom (e.g. at 2% a 30-frame clip is ~4px wide). Target width
    // is what a default clip spans at 100% (DefaultClipLen * DefaultPxPerFrame px); never shorter than
    // DefaultClipLen, so zooming in keeps the old behaviour. Still clamped to the gap by the caller.
    private int DefaultNewClipLen()
    {
        float wantedPx = DefaultClipLen * DefaultPxPerFrame;
        int len = (int)MathF.Round(wantedPx / _pxPerFrame);
        return Math.Max(DefaultClipLen, len);
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
        int wanted = DefaultNewClipLen();
        int len = rightStart == int.MaxValue ? wanted : Math.Min(wanted, rightStart - start);
        if (len < 1) return;

        _clipCounter++;
        var nc = new ClipData { Id = InterpoleDocument.NewId("cl"), Label = "clip " + _clipCounter, Start = start, Length = len };
        tr.Clips.Add(nc);
        SelectSingle(nc.Id);
        Commit();
    }

    // Double-clicking an empty spot on an audio track opens a file dialog; the chosen sound file
    // becomes a clip sized to the file's duration (in frames at Fps), clamped to the gap on the track.
    // Mirrors AddClip's neighbour/gap math. The file dialog is modal (blocks this frame) — acceptable
    // for a one-shot picker. FileInPoint starts at 0 (play from the file's head); trim it in the
    // inspector or by dragging the clip's left edge.
    private void AddAudioClip(TrackData tr, float frameF)
    {
        int f = Math.Max(0, (int)MathF.Round(frameF));
        int leftEnd = 0, rightStart = int.MaxValue;
        foreach (var c in tr.Clips)
        {
            if (ClipEnd(c) <= f) leftEnd = Math.Max(leftEnd, ClipEnd(c));
            else if (c.Start >= f) rightStart = Math.Min(rightStart, c.Start);
            else return; // f is inside an existing clip
        }
        int start = Math.Max(f, leftEnd);
        int gap = rightStart == int.MaxValue ? int.MaxValue : rightStart - start;
        if (gap < 1) return;

        string? path = NativeFileDialog.Open("Open audio file", NativeFileDialog.AudioFilter);
        if (path is null) return;

        double dur = WaveformCache.ProbeDurationSeconds(path);
        int fileLen = Math.Max(1, (int)MathF.Round((float)(dur * _fps)));
        int len = gap == int.MaxValue ? fileLen : Math.Min(fileLen, gap);
        if (len < 1) return;

        _clipCounter++;
        var nc = new ClipData
        {
            Id = InterpoleDocument.NewId("cl"),
            Label = System.IO.Path.GetFileNameWithoutExtension(path),
            Start = start, Length = len,
            Color = 2,   // green-ish default so audio clips read as audio; recolour freely
            FilePath = path, FileInPoint = 0, Gain = 1f, FileDurationSeconds = dur,
        };
        tr.Clips.Add(nc);
        WaveformCache.Get(path, out _);   // kick off the background peak decode now
        SelectSingle(nc.Id);
        Commit();
    }

    private void RemoveClip(TrackData tr, ClipData clip)
    {
        tr.Clips.Remove(clip);
        _selection.Remove(clip.Id);
        _waveEnv.Remove(clip.Id);   // drop any cached waveform envelope for this clip
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
        foreach (var id in _selection) _waveEnv.Remove(id);   // drop cached waveform envelopes
        _selection.Clear();
        _selectedClipId = null;
        if (removed > 0) Commit();
    }

    // ---- copy / paste ----

    private static ClipData CloneClip(ClipData c) => new()
    {
        Label = c.Label, Start = c.Start, Length = c.Length,
        Color = c.Color, FadeIn = c.FadeIn, FadeOut = c.FadeOut,
        FilePath = c.FilePath, FileInPoint = c.FileInPoint, Gain = c.Gain,
        FileDurationSeconds = c.FileDurationSeconds,
        FloatTracks = CloneFloatTracks(c.FloatTracks)
    };

    // Deep-copy a clip's float (keyframe) tracks with fresh ids for the tracks and their keyframes, so a
    // cloned / pasted clip carries its own independent animation instead of losing it (or sharing ids
    // with the source, which would confuse the editor's keyframe selection).
    private static List<FloatTrackData> CloneFloatTracks(List<FloatTrackData> tracks)
    {
        var copy = new List<FloatTrackData>(tracks.Count);
        foreach (var t in tracks)
        {
            var nt = new FloatTrackData { Id = InterpoleDocument.NewId("ft"), Name = t.Name, Value = t.Value };
            foreach (var k in t.Keys)
                nt.Keys.Add(new Keyframe { Id = InterpoleDocument.NewId("kf"), Frame = k.Frame, Value = k.Value, Interp = k.Interp });
            copy.Add(nt);
        }
        return copy;
    }

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
            // Keep the audio/standard invariant even if the track layout changed since copying: an
            // audio clip only pastes onto an audio track and a standard clip only onto a standard one.
            if ((tr.Kind == TrackKind.Audio) != (e.Clip.FilePath is not null)) continue;
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
                Color = e.Clip.Color, FadeIn = e.Clip.FadeIn, FadeOut = e.Clip.FadeOut,
                FilePath = e.Clip.FilePath, FileInPoint = e.Clip.FileInPoint, Gain = e.Clip.Gain,
                FileDurationSeconds = e.Clip.FileDurationSeconds,
                FloatTracks = CloneFloatTracks(e.Clip.FloatTracks)
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

    // ============================== markers ==============================

    // The marker lane sits in [_rulerBottom, _lanesTop): a full-width strip of named points on the
    // frame axis (global, not per-track). Each marker is a palette-coloured Unity-style pin with its
    // label; a Stop marker is drawn as an outline-only pin (no fill) so it reads distinctly, and a
    // Jump marker adds a direction chevron. Hover/drag brightens one.
    private void DrawMarkerLane(ImDrawListPtr dl, Vector2 mouse)
    {
        float top = _rulerBottom, bottom = _lanesTop;

        dl.PushClipRect(new Vector2(_laneLeft, top), new Vector2(LaneRight, bottom), true);
        dl.AddRectFilled(new Vector2(_laneLeft, top), new Vector2(LaneRight, bottom), Col(0.10f, 0.14f, 0.17f));

        bool overLane = InRect(mouse, new Vector2(_laneLeft, top), new Vector2(LaneRight, bottom));
        var hover = (overLane && _drag == DragMode.None && !_markerDialogOpen) ? HitMarker(mouse) : null;

        const float mkHW = 8f;   // half-width of the marker pin (Unity-style: rounded-top rect → point)
        foreach (var m in Document.Markers)
        {
            float x = FrameToX(m.Frame);
            if (x < _laneLeft - 60f || x > LaneRight + 4f) continue;
            bool hot = (hover is not null && hover.Id == m.Id) || _dragMarkerId == m.Id;
            var (cr, cg, cb) = InterpolePalette.Rgb(m.Color);
            uint col = Col(cr, cg, cb, hot ? 1f : 0.92f);

            // Marker icon (Unity-style pin): a rounded-top rectangle for the upper ~60% of the pin that
            // tapers to a point at the bottom, centred on the frame; a fixed height centred in the lane.
            float mkTop = top + (MarkerLaneHeight - MarkerPinHeight) * 0.5f;
            float mkBot = mkTop + MarkerPinHeight;
            float mkMid = mkTop + MarkerPinHeight * 0.6f;
            // Stop markers render as an outline-only pin (no fill) so they read distinctly as a stop;
            // every other marker is a solid palette-coloured pin. Jump markers add a direction chevron.
            if (m.Stop)
            {
                // Outline-only pin (no fill), traced around the rounded-top-rect-plus-point shape.
                float t = hot ? 2f : 1.5f;
                var tl = new Vector2(x - mkHW, mkTop); var tr = new Vector2(x + mkHW, mkTop);
                var ml = new Vector2(x - mkHW, mkMid); var mr = new Vector2(x + mkHW, mkMid);
                var pt = new Vector2(x, mkBot);
                dl.AddLine(tl, tr, col, t);
                dl.AddLine(tr, mr, col, t);
                dl.AddLine(mr, pt, col, t);
                dl.AddLine(pt, ml, col, t);
                dl.AddLine(ml, tl, col, t);
            }
            else
            {
                dl.AddRectFilled(new Vector2(x - mkHW, mkTop), new Vector2(x + mkHW, mkMid), col, 1.5f, ImDrawFlags.RoundCornersTop);
                dl.AddTriangleFilled(new Vector2(x - mkHW, mkMid), new Vector2(x + mkHW, mkMid), new Vector2(x, mkBot), col);
                // Jump markers get a bright chevron pointing the way the playhead seeks (left = a backward
                // loop-back, right = a forward skip), so the behaviour reads independently of the fill colour.
                if (!string.IsNullOrEmpty(m.JumpTargetId))
                {
                    uint ring = Col(0.97f, 0.97f, 0.98f, hot ? 1f : 0.85f);
                    int tgt = Document.FrameOfMarker(m.JumpTargetId);
                    bool back = tgt >= 0 && tgt < m.Frame;   // loop-back vs forward skip
                    float cy = mkTop + MarkerPinHeight * 0.30f;
                    const float a = 3.5f;
                    float tipX = back ? x - a * 0.5f : x + a * 0.5f;
                    float tailX = back ? x + a * 0.5f : x - a * 0.5f;
                    dl.AddLine(new Vector2(tailX, cy - a), new Vector2(tipX, cy), ring, 1.5f);
                    dl.AddLine(new Vector2(tipX, cy), new Vector2(tailX, cy + a), ring, 1.5f);
                }
            }

            if (!string.IsNullOrEmpty(m.Name))
            {
                var ts = IG.CalcTextSize(m.Name);
                dl.AddText(new Vector2(x + mkHW + 9f, top + (MarkerLaneHeight - ts.Y) * 0.5f),
                           Col(0.92f, 0.94f, 0.96f, hot ? 1f : 0.85f), m.Name);
            }
        }
        dl.PopClipRect();

        // Header cell beside the lane.
        dl.PushClipRect(new Vector2(_origin.X, top), new Vector2(_laneLeft, bottom), true);
        dl.AddRectFilled(new Vector2(_origin.X, top), new Vector2(_laneLeft, bottom), Col(0.12f, 0.15f, 0.18f));
        var lt = IG.CalcTextSize("MARKERS");
        dl.AddText(new Vector2(_origin.X + 14, top + (MarkerLaneHeight - lt.Y) * 0.5f), Col(0.55f, 0.62f, 0.70f), "MARKERS");
        dl.PopClipRect();

        dl.AddLine(new Vector2(_laneLeft, top), new Vector2(_laneLeft, bottom), Col(0f, 0f, 0f, 0.5f));
        dl.AddLine(new Vector2(_origin.X, bottom), new Vector2(LaneRight, bottom), Col(0f, 0f, 0f, 0.4f));
    }

    // The marker under the cursor (its flag/stem or label), nearest flag wins when regions overlap;
    // null if none. Only valid within the marker lane's y-band.
    private MarkerData? HitMarker(Vector2 mouse)
    {
        if (mouse.Y < _rulerBottom || mouse.Y >= _lanesTop) return null;
        MarkerData? best = null;
        float bestDx = float.MaxValue;
        foreach (var m in Document.Markers)
        {
            float x = FrameToX(m.Frame);
            float w = string.IsNullOrEmpty(m.Name) ? 0f : IG.CalcTextSize(m.Name).X + 12f;
            if (mouse.X >= x - 9f && mouse.X <= x + 9f + w)
            {
                float dx = MathF.Abs(mouse.X - x);
                if (dx < bestDx) { bestDx = dx; best = m; }
            }
        }
        return best;
    }

    private MarkerData? FindMarker(string? id)
        => id is null ? null : Document.Markers.FirstOrDefault(m => m.Id == id);

    // Open the modal to place a NEW marker at the given frame — nothing is added until OK is pressed.
    private void OpenNewMarkerDialog(float frameF)
    {
        _markerDialogId = null;
        _markerDialogFrame = Math.Max(0, (int)MathF.Round(frameF));
        _markerNameBuffer = "marker " + (Document.Markers.Count + 1);
        _markerStopBuffer = false;
        _markerJumpTargetBuffer = "";
        _markerColorBuffer = 4;   // orange by default, distinct from the default blue clip
        _markerDialogOpen = _markerDialogRequestOpen = _markerDialogFocus = true;
    }

    // Open the same modal to edit an existing marker (name, colour, and the Stop/Jump action).
    private void OpenEditMarkerDialog(MarkerData m)
    {
        _markerDialogId = m.Id;
        _markerDialogFrame = m.Frame;
        _markerNameBuffer = m.Name ?? "";
        _markerStopBuffer = m.Stop;
        _markerJumpTargetBuffer = m.JumpTargetId ?? "";
        _markerColorBuffer = m.Color;
        _markerDialogOpen = _markerDialogRequestOpen = _markerDialogFocus = true;
    }

    // OK pressed: add the new marker (or apply the edits to the existing one) and commit one undo step.
    private void CommitMarkerDialog()
    {
        string name = _markerNameBuffer.Trim();
        if (_markerDialogId is null)
            Document.Markers.Add(new MarkerData
            {
                Id = InterpoleDocument.NewId("mk"),
                Name = name,
                Frame = Math.Max(0, _markerDialogFrame),
                Stop = _markerStopBuffer,
                JumpTargetId = _markerStopBuffer ? "" : _markerJumpTargetBuffer,
                Color = _markerColorBuffer,
            });
        else
        {
            var m = FindMarker(_markerDialogId);
            // Stop and Jump are mutually exclusive actions; the dialog keeps the buffers consistent.
            if (m is not null)
            {
                m.Name = name;
                m.Stop = _markerStopBuffer;
                m.JumpTargetId = _markerStopBuffer ? "" : _markerJumpTargetBuffer;
                m.Color = _markerColorBuffer;
            }
        }
        Commit();
    }

    private void RemoveMarker(MarkerData m)
    {
        Document.Markers.Remove(m);
        if (_selectedMarkerId == m.Id) _selectedMarkerId = null;
        // Drop any jump references to the marker just removed, so no marker points at a gone target.
        foreach (var other in Document.Markers)
            if (other.JumpTargetId == m.Id) other.JumpTargetId = "";
        Commit();
    }

    // Drag a marker along the frame axis (Shift snaps to clip edges / the playhead, like a clip move).
    // Commits one undo step on release.
    private void ContinueMarkerDrag(Vector2 mouse)
    {
        var m = FindMarker(_dragMarkerId);
        if (!IG.IsMouseDown(ImGuiMouseButton.Left) || m is null)
        {
            _drag = DragMode.None;
            _dragMarkerId = null;
            _clipSnapActive = false;
            if (m is not null) Commit();
            return;
        }

        int f = Math.Max(0, (int)MathF.Round(XToFrame(mouse.X) - _dragGrabFrame));
        if (IG.GetIO().KeyShift)
        {
            CollectClipEdgeTargets(null);
            _snapTargets.Add(Document.Playhead);
            var s = Snap(f);
            if (s.snapped) { f = s.frame; _clipSnapActive = true; _clipSnapFrame = f; }
            else _clipSnapActive = false;
        }
        else _clipSnapActive = false;
        m.Frame = f;
    }

    // Drag a track header up/down to reorder it. The row under the cursor is the drop target; the
    // dragged track is spliced into that slot live so lanes reflow beneath the mouse. A single Commit
    // on release makes the whole reorder one undo step (unchanged order pushes no history).
    private void ContinueTrackDrag(Vector2 mouse)
    {
        int from = Document.Tracks.FindIndex(t => t.Id == _dragTrackId);
        if (!IG.IsMouseDown(ImGuiMouseButton.Left) || from < 0)
        {
            _drag = DragMode.None;
            _dragTrackId = null;
            if (from >= 0) Commit();
            return;
        }

        // Reorder only when the cursor crosses fully past the dragged row's top / bottom edge, so a
        // plain click (cursor still inside its own row) never moves it and there's no mid-row jump.
        // Loop the single-step swaps so a fast drag spanning several rows still keeps up.
        while (from > 0 && mouse.Y < TrackTop(from))
        {
            (Document.Tracks[from - 1], Document.Tracks[from]) = (Document.Tracks[from], Document.Tracks[from - 1]);
            from--;
        }
        while (from < Document.Tracks.Count - 1 && mouse.Y > TrackTop(from) + TrackHeightOf(from))
        {
            (Document.Tracks[from + 1], Document.Tracks[from]) = (Document.Tracks[from], Document.Tracks[from + 1]);
            from++;
        }
    }

    // Reorder float tracks on the clip page by dragging their headers — the clip-page twin of
    // ContinueTrackDrag, operating on the active clip's FloatTracks (uniform rows from _lanesTop).
    private void ContinueFloatTrackDrag(Vector2 mouse)
    {
        if (FindClip(_activeClipId) is not { } fc) { _drag = DragMode.None; _dragTrackId = null; return; }
        var list = fc.clip.FloatTracks;
        int from = list.FindIndex(t => t.Id == _dragTrackId);
        if (!IG.IsMouseDown(ImGuiMouseButton.Left) || from < 0)
        {
            _drag = DragMode.None;
            _dragTrackId = null;
            if (from >= 0) Commit();
            return;
        }

        // Swap only when the cursor crosses fully past the dragged row's top / bottom edge, so a plain
        // click never moves it. Loop the single-step swaps so a fast drag over several rows keeps up.
        while (from > 0 && mouse.Y < FloatRowTop(from))
        {
            (list[from - 1], list[from]) = (list[from], list[from - 1]);
            from--;
        }
        while (from < list.Count - 1 && mouse.Y > FloatRowTop(from) + TrackHeight)
        {
            (list[from + 1], list[from]) = (list[from], list[from + 1]);
            from++;
        }
    }

    // The marker create/edit modal: name, colour, and the on-reach action (Stop or Jump-to-marker,
    // mutually exclusive). Shown before a new marker is placed and reused to edit an existing one.
    // Managed as an ImGui popup so it captures
    // input while open; _markerDialogOpen gates the editor's own interaction/shortcuts meanwhile.
    private void DrawMarkerDialog()
    {
        const string id = "Marker##interpole";
        if (_markerDialogRequestOpen) { IG.OpenPopup(id); _markerDialogRequestOpen = false; }
        if (!_markerDialogOpen) return;

        Vector2 disp = IG.GetIO().DisplaySize;
        IG.SetNextWindowPos(new Vector2(disp.X * 0.5f, disp.Y * 0.5f), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        bool keepOpen = true;   // the title-bar close (X) flips this → treated as Cancel via IsPopupOpen below
        if (IG.BeginPopupModal(id, ref keepOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            IG.TextDisabled((_markerDialogId is null ? "New marker at frame " : "Edit marker at frame ") + _markerDialogFrame);
            IG.Dummy(new Vector2(0f, 4f));
            IG.SetNextItemWidth(240f);
            if (_markerDialogFocus) { IG.SetKeyboardFocusHere(); _markerDialogFocus = false; }
            bool enter = IG.InputText("Name", ref _markerNameBuffer, 128, ImGuiInputTextFlags.EnterReturnsTrue);

            // Colour swatches from the shared clip palette; the selected one gets a white outline.
            IG.Dummy(new Vector2(0f, 2f));
            IG.TextDisabled("Colour");
            var pdl = IG.GetWindowDrawList();
            for (int i = 0; i < InterpolePalette.Count; i++)
            {
                if (i > 0) IG.SameLine();
                var (r, g, b) = InterpolePalette.Rgb(i);
                if (IG.ColorButton("##mkcol" + i, new Vector4(r, g, b, 1f),
                        ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new Vector2(18f, 18f)))
                    _markerColorBuffer = i;
                if (_markerColorBuffer == i)
                    pdl.AddRect(IG.GetItemRectMin() - new Vector2(1f, 1f), IG.GetItemRectMax() + new Vector2(1f, 1f),
                                Col(1f, 1f, 1f, 0.95f), 0f, ImDrawFlags.None, 2f);
            }
            IG.Dummy(new Vector2(0f, 4f));

            // Stop and Jump are mutually exclusive actions on reaching the marker: setting one clears
            // the other's buffer so OK never commits both.
            if (IG.Checkbox("Stop playback when reached", ref _markerStopBuffer) && _markerStopBuffer)
                _markerJumpTargetBuffer = "";

            IG.Dummy(new Vector2(0f, 2f));
            IG.TextDisabled("Jump to marker");
            IG.SetNextItemWidth(240f);
            if (IG.BeginCombo("##mkJump", MarkerLabel(FindMarker(_markerJumpTargetBuffer))))
            {
                if (IG.Selectable("(none)", string.IsNullOrEmpty(_markerJumpTargetBuffer)))
                    _markerJumpTargetBuffer = "";
                foreach (var t in Document.Markers)
                {
                    if (t.Id == _markerDialogId) continue;   // a marker can't jump to itself
                    if (IG.Selectable(MarkerLabel(t) + "##" + t.Id, _markerJumpTargetBuffer == t.Id))
                    { _markerJumpTargetBuffer = t.Id; _markerStopBuffer = false; }
                }
                IG.EndCombo();
            }

            IG.Dummy(new Vector2(0f, 6f));
            bool ok = IG.Button("OK", new Vector2(100f, 0f)) || enter;
            IG.SameLine();
            bool cancel = IG.Button("Cancel", new Vector2(100f, 0f)) || IG.IsKeyPressed(ImGuiKey.Escape, false);
            if (ok) { CommitMarkerDialog(); IG.CloseCurrentPopup(); }
            else if (cancel) IG.CloseCurrentPopup();
            IG.EndPopup();
        }
        if (!IG.IsPopupOpen(id)) _markerDialogOpen = false;
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
        => new InterpoleDocument { Title = Document.Title, Tracks = Document.Tracks, Markers = Document.Markers, Playhead = 0 }.Serialize();

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
        Document.Markers = snap.Markers;       // markers are structural too (undo/redo covers them)

        _drag = DragMode.None;
        _dragClipId = null;
        _panning = false;
        _hoverClipId = null;
        _editKind = EditKind.None;

        Changed?.Invoke();
    }

    // ============================== transport / playback ==============================

    // Transport control from the node's input pins — identical effect to the on-screen buttons /
    // Space. Play/Stop/Rewind are momentary, so the node fires them on a rising edge.
    public void Play() => OnTransport(TButton.Play);
    public void Stop() => OnTransport(TButton.Stop);
    public void Rewind() => OnTransport(TButton.Rewind);

    // Seek the playhead to an absolute frame — the programmatic equivalent of a ruler scrub, fired by
    // the node's rising-edge Set pin (which reads the Time pin's value). One-shot: it does NOT suppress
    // playback (if playing, playback simply continues from the new frame). Persists like a scrub (calls
    // Changed — one save per Set, not per frame), and works in No UI Mode (it just sets Playhead).
    public void SeekTo(int frame)
    {
        Document.Playhead = Math.Max(0, frame);
        EnsurePlayheadVisible();
        Changed?.Invoke();
    }

    // Loop is a two-way-bound state: the node reads it (to write the Loop pin back) and sets it (from
    // the pin), keeping the pin and the on-screen Loop button in sync. The button still flips _loop
    // via OnTransport(TButton.Loop).
    public bool Loop { get => _loop; set => _loop = value; }

    // Whether the timeline is currently playing — exposed for the node's "Is Playing" output.
    public bool IsPlaying => _playing;

    // The "active" marker for the node's Active Marker output: the name of the marker the playhead is on
    // this frame. A stop marker parks the playhead on its frame, so it stays active for as long as
    // playback is halted there — the intended use ("which marker stopped me"). A plain marker is active
    // only for the single frame the playhead sits on or crosses it during playback (_crossedMarkerName
    // catches one skipped over by a multi-frame advance). "" when the playhead is on no marker. Resolved
    // from the live document, so a rename is reflected; if several markers share the frame, the first wins.
    public string ActiveMarkerName
    {
        get
        {
            int f = Document.Playhead;
            foreach (var m in Document.Markers)
                if (m.Frame == f) return m.Name;
            return _playing ? (_crossedMarkerName ?? "") : "";
        }
    }

    private void OnTransport(TButton b)
    {
        switch (b)
        {
            case TButton.Play:
                // Parked outside where playback would run? Jump to the start first, so Play never
                // stays stuck. When looping, "start" is the loop-in; otherwise frame 0.
                if (_loop)
                {
                    var (li, lo) = Document.EffectiveLoop();
                    if (Document.Playhead >= lo || Document.Playhead < li) { Document.Playhead = li; EnsurePlayheadVisible(); Changed?.Invoke(); }
                }
                else if (Document.Playhead >= ContentEnd()) { Document.Playhead = 0; EnsurePlayheadVisible(); Changed?.Invoke(); }
                _playing = true; _playAccum = 0f;
                break;
            case TButton.Stop: _playing = false; _playAccum = 0f; Changed?.Invoke(); break;        // save where it stopped
            // Jump to the start + bring it into view. When looping, "start" is the loop-in (so Rewind
            // returns to the top of the loop region), otherwise frame 0.
            case TButton.Rewind:
                Document.Playhead = _loop ? Document.EffectiveLoop().inFrame : 0;
                _playAccum = 0f; EnsurePlayheadVisible(); Changed?.Invoke();
                break;

            case TButton.Loop: _loop = !_loop; break;
        }
    }

    // Drive playback from the node's Update, once per frame, independent of the ImGui frame — so the
    // timeline keeps playing (and the outputs keep updating) in No UI Mode, which skips the editor
    // draw entirely. dt comes from a wall clock (there is no io.DeltaTime without a frame); it is
    // sampled every call so it stays a single frame's worth even when not playing. Scrubbing the
    // playhead (UI only) still suppresses the advance, same as before.
    public void Tick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)(now - _lastTickSec);
        _lastTickSec = now;
        if (_playing && _drag != DragMode.Playhead) AdvancePlayback(dt);
    }

    // The playhead as a *continuous* (sub-frame) frame position: the integer playhead plus the
    // fractional-frame accumulator from real-time playback. The node feeds this to the audio mixer as
    // its chase target, so the audio follows a smooth position instead of a frame-stepped one (which
    // would make the varispeed correction warble). Falls back to the integer playhead when parked
    // (_playAccum is reset on Play / Stop / Rewind).
    public double PlayheadContinuousFrames => Document.Playhead + _playAccum;

    // Advance the playhead while playing. Default: by real elapsed time (dt × Fps), accumulating
    // fractional frames so 1s = 1s no matter the render rate. Legacy FrameStepPlayback mode: one
    // document-frame per rendered frame (render-rate bound). Deliberately NOT persisted per step
    // (that would call SetPinValue every frame); the position is saved on Stop / Rewind / scrub.
    private void AdvancePlayback(float dt)
    {
        // A crossed plain marker is only ever valid for the single advance step that set it.
        _crossedMarkerName = null;

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

        // Act on the earliest action marker the playhead is about to cross this frame — checked before
        // the wrap/end logic. When looping, only the span up to the loop-out is crossed linearly (it
        // wraps at lo), so markers beyond it don't count this step. Markers strictly after the current
        // frame qualify, so playback can resume off (or jump straight back onto) an action marker.
        int prev = Document.Playhead;
        long target = (long)prev + advance;
        int scanEnd = _loop
            ? (int)Math.Min(target, (long)Document.EffectiveLoop().outFrame)
            : (int)Math.Min(target, (long)end);
        var action = Document.EarliestActionMarkerIn(prev, scanEnd);
        if (action is not null)
        {
            int jumpTo = action.Stop ? -1 : Document.FrameOfMarker(action.JumpTargetId);
            if (jumpTo >= 0)
            {
                // Jump marker: seek to the target marker's frame and keep playing. A backward target
                // loops forever until another event (Stop / state change / Set / Rewind) moves us.
                Document.Playhead = jumpTo;
                _playAccum = 0f;   // don't carry this frame's fractional overshoot across the jump
                Changed?.Invoke();
                return;
            }
            // Stop marker (or a jump whose target has been deleted): park on it and halt, as if Stop
            // were pressed there. The node reports it as the active marker via the parked playhead.
            Document.Playhead = action.Frame;
            _playing = false;
            _playAccum = 0f;
            Changed?.Invoke();
            return;
        }

        // No action marker in the crossed span: note the last plain marker the playhead passes this step
        // (largest frame in (prev, scanEnd]), so the node can flag it active for this one frame even when
        // a multi-frame advance skipped its exact frame. A marker we land on is reported via the playhead.
        MarkerData? crossed = null;
        foreach (var m in Document.Markers)
            if (m.Frame > prev && m.Frame <= scanEnd && (crossed is null || m.Frame > crossed.Frame))
                crossed = m;
        _crossedMarkerName = crossed?.Name;

        long np = (long)Document.Playhead + advance;
        if (_loop)
        {
            // Wrap within the effective loop region [li, lo). Before lo it just advances; at/past lo
            // it folds back into the region, carrying any multi-frame overshoot.
            var (li, lo) = Document.EffectiveLoop();
            if (np < lo) { Document.Playhead = (int)np; return; }
            int span = lo - li;
            long rel = (np - li) % span;
            if (rel < 0) rel += span;
            Document.Playhead = (int)(li + rel);
            return;
        }

        if (np < end) { Document.Playhead = (int)np; return; }
        Document.Playhead = end; _playing = false; _playAccum = 0f;   // played to the content end, stop
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
    // Delegates to the document so playback, loop fallback, and any future consumer share one source.
    private int ContentEnd() => Document.ContentEnd();

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

    private void StartEditFloatTrackName(FloatTrackData ft)
    {
        _editKind = EditKind.FloatTrackName;
        _editId = ft.Id;
        _editBuffer = ft.Name ?? "";
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
            pos = new Vector2(_origin.X + 10, top + TrackHeightOf(i) * 0.5f - IG.GetFrameHeight() * 0.5f);
            width = HeaderWidth - 18;
        }
        else if (_editKind == EditKind.FloatTrackName)
        {
            if (FindClip(_activeClipId) is not { } fc) { _editKind = EditKind.None; return; }
            int i = fc.clip.FloatTracks.FindIndex(t => t.Id == _editId);
            if (i < 0) { _editKind = EditKind.None; return; }
            float top = FloatRowTop(i);
            pos = new Vector2(_origin.X + 12, top + TrackHeight * 0.5f - IG.GetFrameHeight() * 0.5f);
            width = FloatHeaderNameWidth();
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
        else if (_editKind == EditKind.FloatTrackName)
        {
            if (FindClip(_activeClipId) is { } fc)
            {
                var ft = fc.clip.FloatTracks.FirstOrDefault(t => t.Id == _editId);
                if (ft is not null) ft.Name = _editBuffer.Trim();
            }
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
        int hh = totalSec / 3600, mm = totalSec / 60 % 60, ss = totalSec % 60, ff = frame % _fps;
        string hms = hh > 0 ? $"{hh}:{mm:00}:{ss:00}" : $"{mm}:{ss:00}";
        return step % _fps == 0 ? hms : $"{hms}:{ff:00}";
    }

    // A "nice" whole-second step (1,2,5,10,15,30,60,...) at least as large as raw seconds, so
    // timecode-mode ruler labels land on readable second boundaries regardless of zoom.
    private static int NiceSecondStep(float rawSec)
    {
        if (rawSec < 1) return 1;
        int[] steps = { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200 };
        foreach (var s in steps)
            if (s >= rawSec) return s;
        int big = 7200;
        while (big < rawSec) big *= 2;
        return big;
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
