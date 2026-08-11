using Stride.Core.Mathematics;
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
// Besides the editor, the node emits per-track domain outputs evaluated at the playhead:
// the current frame, the active clip's name, its fade gain (0..1), and its palette colour —
// aligned Spreads, one entry per track. An optional external Time/playhead *input* is still to
// come (see DEVNOTES → Next steps).
[ProcessNode(Name = "Interpole", Category = "Interpole")]
public class InterpoleNode : IDisposable
{
    // The input pin (string, JSON) that stores the document *in the patch*. Must match the
    // pin name vvvv derives from the 'data' parameter of Update.
    private const string DataPinName = "Data";

    // The Loop pin is two-way bound: the node writes the editor's loop state back into it (same
    // SetPinValue technique as Data), so the pin and the on-screen Loop button stay in sync. Must
    // match the name vvvv derives from the 'loop' parameter.
    private const string LoopPinName = "Loop";

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

    // Previous transport-pin states, for rising-edge (false→true) detection so each pin fires its
    // action once per press — a bang or a held bool both work like clicking the on-screen button.
    private bool _prevPlay, _prevStop, _prevRewind;

    // Last synced value of the two-way Loop state (a plain bool, not a bang). Guards both directions
    // of the sync so a pin change we just applied — or an editor toggle we just wrote back — doesn't
    // ping-pong.
    private bool _prevLoop;

    // Previous Loop In / Loop Out pin values, so the region is pushed to the document only when a
    // pin *changes* — this lets the pins and the on-screen loop ribbon coexist (last writer wins)
    // instead of the pins overwriting a ribbon edit every frame.
    private int _prevLoopIn, _prevLoopOut;

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
        // Transport control, same as the on-screen buttons / Space. Play/Stop/Rewind fire on a
        // rising edge (false→true), so wire a bang or toggle a bool: Play starts playback (restarting
        // from 0 if parked at the end); Stop halts and saves the playhead; Rewind jumps to frame 0.
        bool play,
        bool stop,
        bool rewind,
        // Looping on/off — a two-way-bound *state* (not a bang): set the pin true/false to drive the
        // editor, and toggling Loop in the editor writes the pin back, so the pin and the on-screen
        // Loop button always agree. The state persists with the patch via the pin.
        bool loop,
        // The loop region in frames, half-open [Loop In, Loop Out). Applied to the timeline whenever
        // either value *changes*, so they coexist with the on-screen loop ribbon (last edit wins). An
        // invalid/empty range (Loop Out <= Loop In, e.g. both 0) means "loop the whole content".
        int loopIn,
        int loopOut,
        out ILayer? editor,
        // The playhead position in frames — the current time on the timeline. Same value the
        // ruler shows; advanced by playback and scrubbing.
        out int currentFrame,
        // True while the timeline is playing (Play pressed and not yet stopped / reached the end).
        out bool isPlaying,
        // Per-track outputs, aligned by track order (index 0 = the top track). For each track,
        // the clip covering the playhead is the "active" clip:
        //   activeClipName — the active clip's label, or "" where the playhead sits in a gap.
        //   clipFrame      — the playhead's 0-based frame position *within* the active clip
        //                    (playhead - clip.Start; 0 at its first frame, Length-1 at its last),
        //                    or -1 in a gap.
        //   clipLength     — the active clip's total length in frames, or -1 in a gap.
        //   fadeGain       — that clip's fade envelope at the playhead (0..1; ramps through the
        //                    fade-in/out, 1 in the middle), or 0 in a gap.
        //   activeColor    — the active clip's palette colour, or transparent (alpha 0) in a gap.
        out Spread<string> activeClipName,
        out Spread<int> clipFrame,
        out Spread<int> clipLength,
        out Spread<float> fadeGain,
        out Spread<Color4> activeColor,
        // Per track (aligned by track order): the name of that track's next clip to play — for
        // preloading what plays next — or "" if the track has none. Normally the soonest clip starting
        // *after* the playhead; **when looping it wraps**: a track with nothing left before Loop Out
        // reports the clip the loop replays from its start, so a looping timeline always looks ahead.
        out Spread<string> upcomingClipNames,
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

        // Transport from the input pins. Play/Stop/Rewind fire on a rising edge (false→true) so a
        // bang or a held bool triggers once, like clicking the button. Loop is different — a two-way
        // *state*: a pin change drives the editor here, and an editor toggle is written back to the
        // pin after the update (below). Done before the editor update so state set this frame is
        // reflected in this frame's draw.
        if (_editor is not null)
        {
            if (play && !_prevPlay) _editor.Play();
            if (stop && !_prevStop) _editor.Stop();
            if (rewind && !_prevRewind) _editor.Rewind();
            // Loop pin changed in the patch → drive the editor's loop state.
            if (loop != _prevLoop) { _editor.Loop = loop; _prevLoop = loop; }
            // Push the loop region only on a pin change, so a ribbon edit isn't stomped every frame.
            if (loopIn != _prevLoopIn || loopOut != _prevLoopOut) _editor.SetLoopFromPins(loopIn, loopOut);
        }
        _prevPlay = play; _prevStop = stop; _prevRewind = rewind;
        _prevLoopIn = loopIn; _prevLoopOut = loopOut;

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

        // Editor-side Loop toggle (button / OnTransport, processed during the update above) → write
        // it back into the Loop pin so the pin reflects the state and it survives save. Guarded by
        // _prevLoop so the value we just applied from the pin isn't echoed back. A no-op outside an
        // editing session (SetPinValue's solution is null in an exported app).
        if (_editor is not null && _editor.Loop != _prevLoop)
        {
            PersistLoopState(_editor.Loop);
            _prevLoop = _editor.Loop;
        }

        isPlaying = _editor?.IsPlaying ?? false;

        // Domain outputs, evaluated at the (possibly just-advanced) playhead. Computed after the
        // editor update so playback that ran this frame is reflected. Available in No UI Mode too
        // (the document still exists; only the ImGui frame is skipped).
        EvaluateOutputs(out currentFrame, out activeClipName, out clipFrame, out clipLength, out fadeGain, out activeColor, out upcomingClipNames);
    }

    // Evaluate the per-track domain outputs from the document at the playhead. Every spread is aligned
    // by track order, so index i corresponds to the same track across all of them.
    private void EvaluateOutputs(out int currentFrame, out Spread<string> names, out Spread<int> clipFrames,
                                 out Spread<int> clipLengths, out Spread<float> gains, out Spread<Color4> colors,
                                 out Spread<string> upcomingNames)
    {
        var doc = _editor?.Document;
        if (doc is null)
        {
            currentFrame = 0;
            names = Spread<string>.Empty;
            clipFrames = Spread<int>.Empty;
            clipLengths = Spread<int>.Empty;
            gains = Spread<float>.Empty;
            colors = Spread<Color4>.Empty;
            upcomingNames = Spread<string>.Empty;
            return;
        }

        int frame = doc.Playhead;
        currentFrame = frame;

        // "Upcoming" wraps when looping: after the wrap the loop replays from its start, so a track
        // with nothing left before Loop Out still has an upcoming clip. Only needs the loop region
        // while looping.
        bool looping = _editor?.Loop ?? false;
        var (loopIn, loopOut) = doc.EffectiveLoop();

        var nb = new SpreadBuilder<string>(doc.Tracks.Count);
        var fb = new SpreadBuilder<int>(doc.Tracks.Count);
        var lb = new SpreadBuilder<int>(doc.Tracks.Count);
        var gb = new SpreadBuilder<float>(doc.Tracks.Count);
        var cb = new SpreadBuilder<Color4>(doc.Tracks.Count);
        var ub = new SpreadBuilder<string>(doc.Tracks.Count);
        foreach (var track in doc.Tracks)
        {
            var clip = track.ActiveClipAt(frame);
            if (clip is null)
            {
                nb.Add("");
                fb.Add(-1);   // sentinel: no active clip in this track at the playhead
                lb.Add(-1);
                gb.Add(0f);
                cb.Add(new Color4(0f, 0f, 0f, 0f));
            }
            else
            {
                var (r, g, b) = InterpolePalette.Rgb(clip.Color);
                nb.Add(clip.Label ?? "");
                fb.Add(frame - clip.Start);   // 0-based position within the clip
                lb.Add(clip.Length);          // total clip length in frames
                gb.Add(clip.GainAt(frame));
                cb.Add(new Color4(r, g, b, 1f));
            }
            // Independent of the active clip: this track's next upcoming clip name ("" if none). When
            // looping it wraps around the loop region so the replayed clips still count as upcoming.
            var upcoming = looping
                ? track.NextClipLooping(frame, loopIn, loopOut)
                : track.NextClipAfter(frame);
            ub.Add(upcoming?.Label ?? "");
        }
        names = nb.ToSpread();
        clipFrames = fb.ToSpread();
        clipLengths = lb.ToSpread();
        gains = gb.ToSpread();
        colors = cb.ToSpread();
        upcomingNames = ub.ToSpread();
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

    // Write the editor's loop state back into the Loop pin's value (same technique as PersistData),
    // so toggling Loop in the editor is reflected on the pin and persists with the patch. A no-op
    // outside an editing session, and harmless if the pin is connected (the connection still wins).
    private void PersistLoopState(bool value)
    {
        var solution = IDevSession.Current?.CurrentSolution;
        if (solution is null) return;

        solution.SetPinValue(_nc.Path.Stack.Peek(), LoopPinName, value).Confirm(JustWriteToThePin);
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
