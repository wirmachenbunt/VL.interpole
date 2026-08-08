using VL.Interpole.Engine;
using VL.ImGui;
using VL.Lang.PublicAPI;
using VL.Lib.Collections;
using VL.Model;
using VL.Skia;
// 'using VL.ImGui' pulls in VL.ImGui.PinAttribute, which collides with the import
// attribute we need here — alias to the VL.Core.Import one (same gotcha as the editor).
using Pin = VL.Core.Import.PinAttribute;

namespace VL.Interpole;

// The Interpole node for vvvv gamma — a [ProcessNode] that renders a clip-timeline editor
// (Premiere-style: track headers on the left, clip lanes on the right) as a Skia ILayer and
// persists the timeline inside the .vl patch. Built on the proven VL.Automata plumbing.
//
// 'Data' is the timeline itself, stored as JSON on an input pin — this is the persistence.
// The node reads it to load the document, and on every edit writes the serialized document
// back into the pin via the solution API (ISolution.SetPinValue), so it is saved *inside the
// .vl patch*, not in an external file (the gamma equivalent of beta's "save data to a hidden
// pin"). Leave 'Data' unconnected — you can still paste a timeline's JSON in to load it.
//
// 'Editor' is the editor as a Skia ILayer; wire it straight into a renderer —
// SetSpaceUpstream2 forces DIPTopLeft space so it renders (and takes input) correctly in the
// renderer's default Normalized Space, no setting to change.
//
// Playback outputs (a time input + per-track active clip) come later — this pass is the
// editor + persistence only.
[ProcessNode(Name = "Interpole", Category = "Interpole")]
public class InterpoleNode : IDisposable
{
    // The input pin (string, JSON) that stores the document *in the patch*. Must match the
    // pin name vvvv derives from the 'data' parameter of Update.
    private const string DataPinName = "Data";

    // Persist into the pin's value without recompiling (~AffectCompilation) or adding an undo
    // step (~AddToHistory): continuous autosave that doesn't disturb the running graph.
    private const SolutionUpdateKind JustWriteToThePin =
        SolutionUpdateKind.Default & ~SolutionUpdateKind.AffectCompilation & ~SolutionUpdateKind.AddToHistory;

    private readonly NodeContext _nc;

    // Wraps the ImGui editor widget as a Skia layer so the node outputs an ILayer directly —
    // no ToSkiaLayer node needed in the patch.
    private readonly ToSkiaLayer _toSkia = new();
    // Forces the editor layer into DIPTopLeft space, so it renders 1:1 from the top-left in
    // the renderer's *default* Normalized space — and re-derives the mouse PositionInWorldSpace
    // so input lines up too (see the VL.Automata notes).
    private readonly SetSpaceUpstream2 _editorSpace = new();
    private readonly ILayer _editorLayer;
    private InterpoleEditorWidget? _editor;

    private string _loadedData = "\0";
    private string _persistedData = "\0";
    private bool _docDirty;

    public InterpoleNode(NodeContext nodeContext)
    {
        _nc = nodeContext;
        _editorSpace.Update(_toSkia, out _editorLayer, CommonSpace.DIPTopLeft);
    }

    public void Update(
        // The timeline as JSON, stored on this pin and saved with the patch. Leave it
        // unconnected — the node reads and writes it. You can still paste JSON in to load it.
        string? data,
        // Headless switch: when true, skip the ImGui frame + ToSkiaLayer update entirely and
        // output a null Editor layer. Flip on in a running/exported patch to reclaim the
        // per-frame editor cost. Default false = UI on.
        bool noUIMode,
        // Inspector-only switch (hidden on the node face, editable in the Inspektor). When true
        // the editor canvas is drawn transparent, so the renderer shows through. Default false.
        [Pin(Visibility = PinVisibility.Optional)]
        bool noBackground,
        out ILayer? editor,
        // Inspector-only (like No Background). Frames per second the editor uses to derive
        // timecode for the "time / frames" ruler toggle — a display timebase only, it does not
        // change the model (which stays in integer frames). Default 60; clamped >= 1 in the editor.
        // (Declared last because vvvv reads the pin's default from the C# default value, which C#
        // only allows on an optional parameter placed after the required 'out'.)
        [Pin(Visibility = PinVisibility.Optional)]
        int fps = 60,
        // Inspector-only (like No Background). Playback timing mode. Default false = real-time:
        // the playhead advances by elapsed time × Fps, so one second of playback is one wall-clock
        // second no matter the render rate. True = legacy: advance one frame per rendered frame
        // (bound to the renderer's FPS). The ruler time/frames toggle is independent of this.
        [Pin(Visibility = PinVisibility.Optional)]
        bool frameStepPlayback = false)
    {
        LoadIfChanged(data ?? "");

        // Apply editor edits: persist the document back into the Data pin so it survives
        // reload / save.
        if (_docDirty && _editor is not null)
        {
            PersistData(_editor.Document.Serialize());
            _docDirty = false;
        }

        if (_editor is null || noUIMode)
        {
            editor = null;
        }
        else
        {
            _editor.ShowBackground = !noBackground;
            _editor.Fps = fps;
            _editor.FrameStepPlayback = frameStepPlayback;
            _toSkia.Update(_editor.Widget, dockingEnabled: false, fonts: Spread<FontConfig?>.Empty, fullscreenWindow: false, Style: null);
            editor = _editorLayer;
        }
    }

    // Load the document from the Data pin into the editor, but only when the pin value is one
    // we don't already have. A fresh/empty pin yields a seeded default timeline, persisted back
    // so a newly dropped node saves a valid document. The _persistedData guard keeps our own
    // autosave echo from rebuilding the editor (which would reset the view).
    private void LoadIfChanged(string data)
    {
        if (_editor is not null && (data == _loadedData || data == _persistedData)) return;
        _loadedData = data;

        var doc = InterpoleDocument.Parse(data);
        EnsureDefault(doc);

        _editor?.Dispose();
        _editor = new InterpoleEditorWidget("Interpole", doc)
        {
            Changed = () => _docDirty = true
        };

        _persistedData = data;
        var canonical = doc.Serialize();
        if (canonical != data) PersistData(canonical);
    }

    // Write the document JSON into the Data pin's value in the .vl document. Skips redundant
    // writes; a no-op outside an editing session (IDevSession.Current is null in an exported
    // app, where the value is already baked in).
    private void PersistData(string json)
    {
        if (json == _persistedData) return;
        _persistedData = json;

        var solution = IDevSession.Current?.CurrentSolution;
        if (solution is null) return;

        solution.SetPinValue(_nc.Path.Stack.Peek(), DataPinName, json).Confirm(JustWriteToThePin);
    }

    // A fresh/empty document is seeded with a couple of tracks and a couple of clips, so a
    // newly dropped node shows a recognisable timeline instead of an empty canvas.
    private static void EnsureDefault(InterpoleDocument doc)
    {
        if (doc.Tracks.Count > 0) return;

        var t1 = new TrackData { Id = InterpoleDocument.NewId("tr"), Name = "Track 1" };
        t1.Clips.Add(new ClipData { Id = InterpoleDocument.NewId("cl"), Label = "clip 1", Start = 0, Length = 30 });
        t1.Clips.Add(new ClipData { Id = InterpoleDocument.NewId("cl"), Label = "clip 2", Start = 45, Length = 20 });
        var t2 = new TrackData { Id = InterpoleDocument.NewId("tr"), Name = "Track 2" };

        doc.Tracks.Add(t1);
        doc.Tracks.Add(t2);
    }

    public void Dispose()
    {
        _editor?.Dispose();
        _toSkia.Dispose();
    }
}
