# VL.Interpole — dev notes

The *why* behind the build, the vvvv-specific gotchas, and a **resume-here** list. Read this
before changing the node or the editor. (Usage lives in [README.md](README.md).)

VL.Interpole is a deliberate **port of VL.Automata's plumbing** (`../AutomataX/vvvvPlugin`).
Anything about ImGui-as-`ILayer`, the DIPTopLeft space fix, or persisting data in the patch is
documented in far more depth in **Automata's `DEVNOTES.md`** — this file records what Interpole
does the same, and where it deliberately differs.

---

## Inherited from Automata (don't re-derive)

- **Target `net8.0-windows` + `EnableWindowsTargeting`.** The node outputs a VL.Skia `ILayer`
  (it wraps the ImGui editor with `VL.ImGui.ToSkiaLayer` in C#), and VL.ImGui.Skia / VL.Skia
  are Windows-only. `EnableWindowsTargeting` lets it still build off-Windows.
- **`[assembly: ImportAsIs]`** in `Properties/AssemblyInfo.cs` so vvvv imports the
  `[ProcessNode]` when the assembly is referenced as a project.
- **`ImGui` namespace shadowing.** Inside `namespace VL.Interpole`, the bare name `ImGui`
  binds to the sibling namespace `VL.ImGui`, not the ImGui.NET static class. Both the editor
  and the node alias it: `using IG = ImGuiNET.ImGui;` (and `using Pin = VL.Core.Import.PinAttribute;`
  in the node, because `using VL.ImGui` pulls in a colliding `PinAttribute`).
- **Editor as `ILayer` in DIPTopLeft space.** The node wraps `ToSkiaLayer` in VL.Skia's
  `SetSpaceUpstream2` with `CommonSpace.DIPTopLeft`. This makes it render **and take mouse
  input** 1:1 from the top-left in a renderer's **default** Normalized space. `ToSkiaLayer`
  sizes ImGui in raw viewport pixels but draws through the incoming transform; `SetSpaceUpstream2`
  also re-derives the mouse `PositionInWorldSpace`, so visuals *and* input line up. Do **not**
  hand-roll a render-only wrapper — you'll get inverted-offset mouse input. (Full analysis in
  Automata's notes.)
- **Persist data in the patch via `ISolution.SetPinValue`.** The `Data` pin holds the timeline
  JSON; the node writes it back with a `SolutionUpdateKind` that skips `AffectCompilation` and
  `AddToHistory` (continuous autosave, no recompile, no undo spam). The write echoes back into
  the live `data` pin over the next frame(s) — `LoadIfChanged` guards against that so our own
  autosave doesn't rebuild the editor (which resets the view **and** reverts the live playhead).
  The guard remembers the last several self-writes (`_selfWrites`), **not just the most recent**:
  a couple of writes can be in flight at once (a `Set`/seek persist plus the rebuild's canonical
  re-persist, or a seek then a Stop), and their echoes arrive interleaved. A single last-value
  guard let a *stale* echo through → a rebuild from old JSON, and the two in-flight literals then
  **ping-ponged the playhead between old and new** (the bug that surfaced once `Set`/seek existed).
  Skipping an echo of *any* recent self-write fixes it; a genuinely new external `data` (never
  written by us) still loads.

## Interpole-specific decisions

- **Units are integer frames.** `ClipData.Start` / `Length` and `Playhead` are `int` frames;
  the ruler ticks in frames. A clip covers the half-open range `[Start, Start+Length)`,
  `Length >= 1`.
- **The placeholder pin-group scaffolding was removed.** The initial groundwork copied
  Automata's dynamic `Fire` pin group (renamed `Trigger`) as scaffolding. When the timeline
  domain landed and we chose editor-only I/O, it was deleted — it mapped to nothing. The
  technique is still documented in Automata + memory; re-add via `ISolution.ModifyPinGroup` if
  a bang-per-track (or similar) is ever wanted.
- **Non-overlap is enforced by neighbour clamping, not by a constraint solver.** Within a
  track, `LeftBound`/`RightBound` return the immediate left neighbour's end and right
  neighbour's start; every single-clip move/resize/add clamps against them. Neighbour identity
  stays stable during a drag because we clamp *before* the clip could cross a neighbour. `AddClip`
  finds the gap under the cursor and shrinks the new clip to fit (skips if < 1 frame). The
  multi-selection move uses `GroupLeftBound`/`GroupRightBound` — same idea, but ignoring the
  *other selected* clips (they move together, so they can't collide with each other).
- **A new clip's default length scales with zoom** (`DefaultNewClipLen`). A fixed `DefaultClipLen` (30
  frames) is a ~4px unusable sliver when zoomed far out (the user works mostly at 2%, where
  `_pxPerFrame`≈0.12), so `AddClip` instead targets the on-screen width a default clip has at 100% zoom
  (`DefaultClipLen * DefaultPxPerFrame`≈180px): `max(DefaultClipLen, round(180 / _pxPerFrame))`. So a new
  clip is 30 frames at ≥100% (zooming in is unchanged) and grows in frames as you zoom out (~1500 at 2%,
  ~3000 at the 1% floor) to stay a comfortable, grabbable width. Still gap-clamped by `AddClip`; audio
  clips (sized to the file) and paste (own length) are unaffected.
- **Clips can be dragged between tracks (single clip or whole selection).** `MoveSelection` is the
  single mover for `DragMode.MoveClip` (a lone clip is just a selection of one). It first tries a
  **whole-track vertical shift** toward the track under the cursor: `trackDelta` is clamped so the
  group stays within `[0, Count)`, and it is committed **only if `GroupFits(trackDelta, timeDelta)`**
  — every selected clip must land on a valid track at a time with no overlap against *non-selected*
  clips there (a uniform shift keeps the selected clips from colliding with each other).
  `ApplyTrackShift` re-homes the clips between `TrackData.Clips` lists (moves collected first, then
  applied, so no list is mutated mid-enumeration). If the group doesn't fit vertically it stays on
  its tracks and only slides horizontally — strict non-overlap means a cross-track drop is
  **exact-fit: no ripple/push**. The shared horizontal delta is then clamped + snapped on the
  (possibly new) tracks. Resize/fade drags stay single-clip.
- **Per-clip fades are lengths in frames, edited by top-corner handles.** `ClipData.FadeIn` /
  `FadeOut` are frame lengths from each end (`[Start, Start+FadeIn)`, `[End-FadeOut, End)`), kept
  `>= 0` with `FadeIn + FadeOut <= Length` (`ClampFades`, also re-run after a resize shrinks the
  clip). `Fades()` returns clamped effective values so drawing/hit-testing is robust to bad
  persisted data. Each clip draws a small handle square on both top corners (`FadeHandleRect` —
  centred on the fade end, clamped inside the clip); the fade renders as a dimmed corner triangle
  plus a gain line from the bottom corner up to the handle. In `HitClip` the fade handles take
  priority over the resize edge they overlap, but only in the top ~11px, so the rest of an edge is
  still resize (new `ClipZone`/`DragMode` FadeIn/FadeOut). The clip fill/border share a `ClipRounding`
  (4px) radius; the fade's dimmed triangle + gain line pull their **corner-anchored** points onto
  that rounded edge (inset `r − r/√2 ≈ 0.293·r` along the diagonal) so they follow the rounded
  corners instead of poking past them. Fades are **document + editor only so far** — no output pin
  applies them yet (see Next steps, alongside the other playback outputs).
- **Inspector is a right-side ImGui child window; the timeline shrinks to make room.** The panel
  edits the selected clip and **starts collapsed** (`_inspectorOpen` default false, session-transient). Layout: `_stripLeft` is the far-right open/close strip (`ToggleStripPx`),
  `_canvasRight` is the timeline's right edge (`= _stripLeft - InspectorWidth` when open, else
  `_stripLeft`), and **`LaneRight` now returns `_canvasRight`** — so every band (ruler, lanes,
  transport, playhead) reflows automatically; nothing else had to change. The rest of the editor is
  hand-drawn DrawList, but the inspector uses **real ImGui widgets** (`InputText`, `ColorButton`,
  `DragInt`/`DragFloat`) inside a `BeginChild`. Gotcha: a **borderless child ignores `WindowPadding`**
  unless `ImGuiChildFlags.AlwaysUseWindowPadding` is passed — without it the widgets sit flush to the
  panel edges. Two things make that coexist with the full-canvas
  `InvisibleButton`: `SetNextItemAllowOverlap()` before the button (so later widgets take input over
  it), and `HandleInteraction` returning early for `mouse.X >= _canvasRight` (the panel/strip region
  is off-limits to timeline hit-testing; the strip click toggles `_inspectorOpen`). `HandleShortcuts`
  bails on `io.WantTextInput` so Ctrl+C/V/Z go to a focused inspector field, not the timeline.
  Numeric fields (`NumberField`) show **frames or seconds** per `_showTimecode` (seconds = frames /
  `Fps`), clamp through `ApplyStart`/`ApplyLength`/`ClampFades` (non-overlap preserved), and Commit
  on `IsItemDeactivatedAfterEdit` (one undo step per field). The name buffer re-seeds from the clip
  whenever the field isn't active, so inline-rename/undo changes show through.
- **Multi-selection (marquee) + clipboard (copy/paste).** Selection is a `HashSet<string>
  _selection` of clip ids, with `_selectedClipId` kept as the **primary** — the inspector's target
  and the bolder outline; other selected clips get a lighter outline. Helpers: `SelectSingle`,
  `ToggleSelected`, `IsSelected`. Ways to select: **click** a clip (single); **Shift/Ctrl-click** a
  clip (`ToggleSelected`); grabbing a clip already in the selection keeps the whole selection (so it
  can group-drag); **drag on an empty lane** = a **marquee** (`DragMode.Marquee`, anchor in screen
  space, clamped to the lane area; `UpdateMarqueeSelection` re-picks every clip whose on-screen rect
  overlaps the band each frame; Shift/Ctrl at drag-start seeds `_marqueeBase` so the marquee *adds*
  to the prior selection). **Del/Backspace** = `DeleteSelection` (one undo step). Selection is by
  id so it survives undo/redo (ids are stable in the JSON); `RemoveClip`/`RemoveTrack` prune stale
  ids and re-home the primary.
  - **The clipboard is a list**, `_clipboard` of `ClipCopy(Clip, TrackIndex, StartOffset)`. `Ctrl+C`
    (`CopySelection`) clones every selected clip — or the hovered one as a fallback — recording each
    clip's track index and its start relative to the earliest copied clip. `Ctrl+V`
    (`PasteClipboard`) rebuilds the group **at the playhead**: the earliest clip lands on the
    playhead (stepped past any clip covering it on its anchor track, as the old single paste did),
    the rest keep their relative track + time; each pasted clip is trimmed to the gap on its track
    and dropped if there's no room, so a paste never overlaps (fades re-clamped after a trim). Pasted
    clips become the new selection; one undo step (`Commit`). Shortcuts live in `HandleShortcuts`,
    suppressed while an inline rename owns the keyboard.
- **Clip colour is a palette index, not an RGB value.** `ClipData.Color` is an `int` index into a
  fixed 10-entry palette (index 0 = the original blue, so old clips are unchanged; out-of-range
  falls back to 0). Storing the index keeps the JSON compact and lets the palette be re-tuned in one
  place. That one place is now **`Engine.InterpolePalette`** (`Colors` + `Rgb`), the single source
  shared by the editor's swatches/clip fill *and* the node's `Active Color` output; the editor's
  `ClipPalette`/`ClipRgb` are thin aliases onto it (so all the old call sites are unchanged). The
  swatch strip is drawn/hit-tested manually in
  `DrawClipColorSwatches` under the clip-rename field (flipped above near the window bottom); a
  swatch click sets `clip.Color` live but keeps the rename open (it deactivates the InputText, so
  the lost-focus is swallowed and the field re-focused) — the colour persists when the rename
  commits, so a rename + recolour is one undo step.
- **The playhead is in the document** (`InterpoleDocument.Playhead`) so it persists and is
  ready to drive a future output. But **playback does not persist per frame** — `AdvancePlayback`
  mutates `Playhead` each rendered frame **without** calling `Changed`, so no `SetPinValue`
  fires during playback (that would hit the solution API ~60×/s). Only manual scrub, **Stop**
  and **Rewind** call `Changed` and save the position. Consequence: if the patch is saved mid-
  playback, the *last saved* playhead is stored, not the live one — acceptable.
- **Playback is driven from the node's `Update`, not the ImGui frame — so it runs in No UI Mode.**
  The editor's `Tick()` advances the playhead once per node `Update` (the node calls it every frame,
  in **both** modes), so **No UI Mode disables only the rendering, not the clock**: the timeline keeps
  playing and the outputs keep updating headless — the whole point of the mode. This was the earlier
  bug — `AdvancePlayback` used to be called from inside `Draw`, which No UI Mode skips, so playback
  froze. `Tick` therefore takes its `dt` from a **wall clock** (`_clock`, a `Stopwatch`), *not* ImGui's
  `io.DeltaTime` (there is no ImGui frame without rendering); `_lastTickSec` is resampled every call so
  the delta is one frame's worth even when parked. Because the node sets `Fps`/`FrameStepPlayback` on
  the editor **before** `Tick` in both modes, playback speed is correct headless too. Scrubbing still
  suppresses the advance (`_drag != DragMode.Playhead`), a no-op in No UI Mode where nothing drags.
- **Playback timebase = real elapsed time (default), render-frames (legacy, hidden).** By default
  `AdvancePlayback(dt)` advances by `dt × Fps` (the wall-clock delta from `Tick`), accumulating fractional
  frames in `_playAccum` so one wall-clock second covers `Fps` frames regardless of render rate —
  so `Fps` is now the timeline's real frame rate, driving **both** playback and the timecode
  display (they finally agree). The old *one-frame-per-Draw* behaviour is kept behind the
  hidden **`Frame Step Playback`** pin (`FrameStepPlayback`) for cases that want it — now one frame per
  node `Update` (so it steps headless too); there `Fps` affects the display only. `_playAccum` is reset
  on Play/Stop/Rewind so a new run never inherits a stale fraction. Loop wraps with `np % end`, carrying
  any multi-frame overshoot.
- **Domain outputs are evaluated in the Engine, emitted per track as aligned `Spread`s.** The pure
  evaluation lives on the model, not the editor: `ClipData.Contains`/`End`/`EffectiveFades`/`GainAt`
  and `TrackData.ActiveClipAt(frame)`. `GainAt` reproduces the editor's fade ramps exactly (linear
  0→1 across `[Start, Start+FadeIn)`, 1→0 across `[End-FadeOut, End)`, 1 between; 0 outside), and the
  editor's own `Fades()` now delegates to `EffectiveFades()` so ramps and gain clamp identically —
  no second copy to drift. The node's `EvaluateOutputs` walks `doc.Tracks` **in order** and builds
  six same-length spreads (`Active Clip Name`, `Clip Frame`, `Clip Length`, `Fade Gain`,
  `Active Color`, `Upcoming Clip Names`) plus scalars `Current Frame` and `Is Playing` (the editor's
  `_playing`, read after the update so a stop-at-end this frame shows), so index *i* is the same track
  across all six; a
  gap yields `""` / `-1` / `-1` / `0` / transparent `Color4(0,0,0,0)`. `Clip Frame` is the
  playhead's 0-based offset inside the active clip (`playhead - clip.Start`) and `Clip Length` is
  the active clip's `Length`; both use `-1` as the "no active clip" sentinel (0 would collide with a
  clip's first frame, and a real `Length` is always >= 1). Colour is Stride `Color4` (RGBA floats — gamma's colour pin type;
  `Stride.Core.Mathematics` is already on VL.Core's graph). Evaluated **after** the editor's
  `ToSkia.Update` so this frame's playback advance is reflected, and computed even in **No UI Mode**
  (the document still exists and `Tick` still advances playback; only the rendering is skipped) so a
  headless/exported patch still plays and gets live outputs.
  Pin-ordering gotcha: the new `out` params are required, so they sit **before** the optional
  `fps`/`frameStepPlayback` (C# forbids a required param after an optional one). **`Upcoming Clip
  Names`** is per-track like the rest (built in the same loop, independent of the active clip):
  `TrackData.NextClipAfter(frame)` returns that track's soonest clip with `Start > frame` (no sort
  assumption — picks the min Start), and the node emits its label or `""` when the track has nothing
  upcoming — for per-track preloading of what plays next. **It wraps while looping** (`_editor.Loop`):
  `NextClipLooping(frame, loopIn, loopOut)` first looks forward within `(frame, loopOut)`, and if the
  track has nothing left before the loop wraps, falls back to `FirstClipInRange(loopIn, loopOut)` — the
  clip the loop replays from its start (covering `Loop In`, else the first clip in the region). So a
  looping timeline always reports an upcoming clip as long as the loop region holds one on that track,
  letting the loop-start content be preloaded ahead of the wrap. Not looping → plain forward
  `NextClipAfter`, `""` past the last clip.
- **On-screen Play button is a toggle + shows playing state (2026-09-05).** The Play button's click
  toggles: pressing it while `_playing` calls `OnTransport(Stop)` (users expect the on-screen play
  control to double as pause/stop), matching the Space shortcut — handled **at the click site**, not in
  `OnTransport(Play)`, which must always *start* (the `Play()` pin/`Active State`/`Jump` paths rely on
  it). While playing, the button takes the same green "engaged" background as the Loop/timecode toggles
  and its triangle turns light green, so its on/off state reads at a glance. Transport icons were also
  shrunk (icon scale `0.26`→`0.21` in `DrawTransportIcon`) for more padding inside each button.
- **Transport input pins: Play/Stop/Rewind are rising-edge, Loop is two-way state.** `Play`/`Stop`/
  `Rewind` are plain `bool` inputs; the node keeps `_prevPlay`/etc. and calls the editor once on each
  false→true transition, so a bang **or** a held/toggled bool behaves like one button press. The
  editor exposes thin `Play()`/`Stop()`/`Rewind()` wrappers over the private `OnTransport(TButton)` —
  the same path the on-screen buttons and Space use, so pin and UI share one implementation
  (restart-from-end, save-on-stop, rewind-to-start — frame 0, or the loop-in when looping — come for free). These run **before** the
  editor update so a Play/Stop set this frame is reflected in this frame's draw. In No UI Mode the state
  is set **and** playback advances — the node's `Tick` drives the playhead every `Update` regardless of
  rendering (see the playback-in-`Update` decision above).
- **Seek from the patch: `Time` (value) + `Set` (trigger), not a continuous drive.** A `Time` int pin
  holds the target frame; a rising-edge `Set` bool applies it — the playhead jumps to `Time` **only** on
  `Set`'s false→true edge (changing `Time` alone does nothing). This was deliberately chosen over a
  continuous "slave the playhead to `Time` every frame" design: a one-shot seek doesn't fight the
  internal transport, needs no override/suppression machinery, and matches the bang-driven feel of the
  other transport pins. Both are plain **required** pins (defaults `0` / `false`, declared **before** the
  `out` params, in the transport group next to Play/Stop/Rewind) — no `-1` sentinel and no after-`out`
  placement is needed, because neither pin's default has to encode an "off" state. The node detects the
  edge with `_prevSet` and calls `_editor.SeekTo(time)` **before** the editor update, alongside the other
  rising-edge transport pins — so it also applies in **No UI Mode** (`SeekTo` just sets `Playhead`, it
  doesn't depend on the ImGui frame). `SeekTo(frame)` sets `Document.Playhead = max(0, frame)`, calls
  `EnsurePlayheadVisible`, and calls **`Changed`** (a seek persists, like a manual scrub — one save per
  `Set`, not per frame, so no `SetPinValue` spam). It deliberately does **not** touch `_playing`: seeking
  while playing just continues playback from the new frame. Domain outputs (`EvaluateOutputs`) read
  `Document.Playhead` after the update, so a `Set` this frame is reflected in this frame's outputs.
- **Loop is a two-way-bound state, synced pin ⇄ editor (like `Data`).** The editor exposes
  `Loop { get; set; }` over `_loop`; the node syncs both directions with a single `_prevLoop` guard.
  **Pin → editor** (before the update): if `loop != _prevLoop`, set `_editor.Loop = loop`. **Editor →
  pin** (after the update, since the on-screen Loop button toggles `_loop` inside the ImGui frame): if
  `_editor.Loop != _prevLoop`, write it back into the **Loop pin** via `ISolution.SetPinValue`
  (`PersistLoopState`, same `~AddToHistory & ~AffectCompilation` write as the `Data` autosave) and
  update `_prevLoop`. The single guard stops a ping-pong: a value we applied from the pin, or wrote
  back from the editor, matches `_prevLoop` next frame and does nothing. Bonus: because the state now
  lives on the pin literal, **loop survives save/reload** (previously `_loop` was purely
  session-transient). If the Loop pin is *connected* upstream, that source wins (the write-back sets
  the shadowed literal but the connection re-drives the editor next frame) — the expected
  "connection is authoritative" behaviour. This replaces the earlier edge-toggle design.
- **Shift snaps the playhead scrub *and* clip move/resize.** Shared helpers: `CollectClipEdgeTargets`
  fills `_snapTargets` with frame 0 + every clip's `Start`/`End`/fade-in end/fade-out start (an
  `exclude` clip is skipped so a dragged clip can't snap to itself); `Snap(frame)` returns the
  nearest target within `SnapPx` (8 px, pixel-based → zoom-independent) plus the pixel distance.
  - **Playhead** (`PlayheadFrameFromMouse`, both ruler click and drag): targets are all clips (no
    exclude, playhead not a target); `_playheadSnapped` tints the playhead cyan + thicker.
  - **Clip drag** (`ContinueClipDrag`): resize snaps the dragged edge against `CollectClipEdgeTargets`
    (excludes the dragged clip, adds the playhead). Move goes through `MoveSelection`, which
    considers the leading *and* trailing edge of **every selected clip** (at the proposed delta)
    against `CollectSnapTargetsExcludingSelection` (excludes the whole selection, adds the playhead),
    takes the closest snap across the group, and applies that one delta to all selected clips — so
    any clip in the selection can snap the move, not just the grabbed one. The snap is dropped if a
    neighbour bound overrides it (`ns == cand` / `== _clipSnapFrame` check after clamp).
    `_clipSnapActive`/`_clipSnapFrame` drive a cyan vertical guide (`DrawSnapGuide`). Note
    the snap state is set in `HandleInteraction` (after the draw calls), so the guide follows by one
    frame — same 1-frame lag as the dragged clip itself, so it stays consistent.
- **Loop region = `[LoopIn, LoopOut)` in the document, with a whole-content fallback.**
  `InterpoleDocument.EffectiveLoop()` returns the explicit stored range when valid (`LoopOut > LoopIn`)
  else `(0, ContentEnd())` — so a doc with no region authored still loops over everything (the old
  behaviour). `AdvancePlayback` wraps within the effective range (`rel = (np - li) % span`, carrying
  overshoot); Play-while-parked restarts at `li` when looping. `ContentEnd()` now lives on the
  document (editor delegates), shared by playback + the fallback. `_playing` stays a session-transient
  widget field; `_loop` is transient too but its value is mirrored to the Loop pin (see the Loop
  two-way-sync note), and the *region* persists in the document.
- **Loop ribbon lives in the top strip of the ruler** (no separate band — it was tried as its own
  16px row, then folded back in). `LoopStripHeight` (14px) carves `_loopBottom = _rulerTop +
  LoopStripHeight` off the top of the ruler; `DrawLoopBar` fills `[_rulerTop, _loopBottom)` over the
  lanes width. The rest of the ruler reflows around it: labels re-center in `[_loopBottom, _lanesTop)`
  and the **playhead handle/line drop to `_loopBottom`** (so the red marker sits below the strip, not
  under it). Interaction splits the ruler by Y: the top strip (`_rulerTop.._loopBottom`) is the loop
  ribbon, below it (`_loopBottom.._lanesTop`) scrubs the playhead — the loop block is checked first so
  it claims its clicks. `DrawLoopBar` renders the effective range as a green bar
  (bright when explicit, faint when the implicit default) with an end grip each side. Interaction adds
  `DragMode.LoopIn/LoopOut/LoopMove`: press hit-tests grips (`EdgeGrabPx`) → body → empty (draw fresh);
  `MaterializeLoop` writes the effective range into the stored fields on grab so a drag edits explicit
  values even when starting from the fallback; Alt/Middle-click clears to `0,0`. `ContinueLoopDrag`
  clamps (in `< out`, min span 1), supports Shift-snap (reusing `CollectClipEdgeTargets` + the
  `_clipSnapActive` guide — a loop drag and clip drag are never concurrent), and discards a zero-span
  scratch on release.
- **The loop region is transport-like, not undoable** (same call as the playhead). It is driven by
  *both* the ribbon and the `Loop In`/`Loop Out` pins, so — like the playhead — it is kept **out of
  `StructuralSnapshot`** and ribbon edits call `Changed` (persist) not `Commit` (no undo step). This
  sidesteps a pin-vs-undo fight (an undo can't revert to a stale loop that the pins won't re-push).
  The node applies the pins only on a **value change** (`_prevLoopIn`/`_prevLoopOut`) via
  `SetLoopFromPins`, so pins and ribbon coexist (last writer wins) instead of the pins stomping a
  ribbon edit every frame. Caveat: a continuously-*animated* loop pin persists on each change (like
  any `Changed`), acceptable for the usual static/occasional use.
- **The stopwatch (time/frames) toggle is display-only.** `_showTimecode` (session-transient, like
  `_playing`/`_loop`; **defaults to `true` = timecode**, and also drives the inspector's frames/
  seconds unit) only changes how the ruler ticks and the transport readout are *labelled* —
  the document stays in integer frames. Timecode is derived from the editor's `Fps` (clamped
  `>= 1`), fed each frame from the node's inspector-only **`Fps` pin** (default 60, same
  `PinVisibility.Optional` treatment as `No Background`). `Fps` also drives real-time playback (see
  the timebase decision above), so the two agree by construction. The *display* toggle is fully
  independent of the *playback* mode — you can watch the ruler in frames while playing in real time
  to see how many frames a second spans. `FramesToTimecode` shows `M:SS` on whole-second ruler
  steps (`step % Fps == 0`) and `M:SS:FF` on sub-second steps; the readout forces the frame
  subfield by passing `step = 1`.
- **Pin default via C# default value → declared after the `out`.** vvvv reads an imported input
  pin's default from the C# parameter's default value (there is *no* `DefaultValue` on the import
  `PinAttribute`). C# forbids an optional parameter (`int fps = 60`) *before* a required `out`, so
  `fps` is declared as the **last** parameter of `Update`, after `out ILayer? editor`. It still
  imports as a normal (inspector-only) input pin — pin direction comes from `out`/`in`, not
  position. Verified working in gamma (shows 60 by default).

- **Undo/redo is the editor's own, snapshot-based.** The `Data`-pin autosave writes with
  `~AddToHistory` (it deliberately stays out of vvvv's undo stack so continuous autosave doesn't
  spam it), so the editor keeps an in-memory history of document snapshots (JSON) instead —
  `_undo`/`_redo` lists + a `_baseline`, capped at `MaxUndo`. Structural mutations route through
  `Commit()` (= `PushUndo()` + `Changed`); `PushUndo` skips no-op steps by comparing snapshots.
  Snapshots are **structural only** — `StructuralSnapshot()` serializes with the playhead zeroed,
  and `ApplySnapshot` restores tracks but **leaves the live `Playhead` as-is** — so undo/redo never
  moves the playhead or fights the transport (playhead scrub / Stop / Rewind call `Changed`
  directly, never `Commit`). `ApplySnapshot` also cancels any in-progress drag/rename (its target
  may no longer exist). All keyboard shortcuts live in `HandleShortcuts`, which bails first on
  `io.WantTextInput` (a focused field owns the keys) and only runs when no inline edit is open:
  **Space** play/stop, **Del/Backspace** delete selection, **B/N** set loop in/out to the playhead
  (`SetLoopInToPlayhead`/`SetLoopOutToPlayhead` — materialise the effective range, clamp the other end
  to stay valid, `Changed` not `Commit` like the rest of the loop editing), **Ctrl+Z** undo,
  **Ctrl+Y / Ctrl+Shift+Z** redo, **Ctrl+C/V** copy/paste the selection.

## Audio tracks (VL.Audio integration)

Audio is **additive and fault-isolated**: everything below only engages for audio *tracks* / *clips*;
a timeline with none behaves byte-for-byte as before. Read this before touching `Engine/
InterpoleAudioEngine.cs`, `Engine/WaveformCache.cs`, or the audio branches in the editor/node.

- **Design decisions:** *full NLE* (multiple audio clips per track, each a file + trim in/out);
  *internal stereo mix bus* → a **stable** `Spread<AudioSignal>` output (not a changing per-clip spread,
  which would churn downstream wiring). **Sync = visual-master, audio soft-follows** (see the sync bullet
  — this replaced an initial *audio-master* clock that jittered the value outputs).
- **The mixer is a `MultiChannelSignal` (2 outputs) → `Spread<AudioSignal>`.** `InterpoleMixerSignal :
  MultiChannelSignal` (base ctor `SetOutputCount(2)` = stereo). VL.Audio's engine pulls it on the
  **audio thread** via `protected override void FillBuffers(float[][] buffers, int offset, int count)`;
  its inherited `Outputs` (one `SingleSignal` per channel) are what the node emits as the `Audio` pin.
  This is exactly how the stock `AudioPlayer` surfaces its channels. **Buffer-offset gotcha:** write
  `buffers[ch][0..count)` and **ignore `offset`** for the destination — `FReadBuffers` is the parent's
  0-based scratch and the per-channel `SingleSignal` copies it out with the right offset (mirrors
  `FileStreamSignal`; writing at `offset+i` corrupts).
- **Sync = the visual playhead is master; audio soft-follows it (varispeed chase).** The playhead is
  driven by the editor's **wall clock** exactly as a no-audio timeline is (`editor.Tick()`), so it — and
  every value output derived from it (Fade Gain, Active Color, Clip Frame…) — stays smooth and locked to
  vvvv's main loop. *(The first cut did the opposite — drove the playhead from the audio sample clock;
  sampling that async clock once per main-loop frame made those outputs visibly **jitter** against the
  rest of the patch. Reverted.)* The node just tells the mixer the playhead each frame as its **chase
  target** (`AudioTransport.TargetSec`), sub-frame precise via **`editor.PlayheadContinuousFrames`**
  (`Playhead + _playAccum` — the fractional accumulator; a *stepped* integer target would make the
  varispeed warble). The mixer keeps its own `double _audioPosSec` and each buffer computes
  `drift = _audioPosSec − TargetSec`: within a 5 ms **deadband** it plays at exactly 1.0; beyond it,
  it nudges playback **speed a few % (`MaxCorrection` 3 %)** to ease back into lock — an inaudible pitch
  bend, no seek; only a **large** jump (`> HardSyncSec` 150 ms — Play from stop, a seek, a loop wrap,
  where `TargetSec` leaps) snaps `_audioPosSec` to the target and forces a reseek. So the audio never
  drives the playhead, yet stays locked within a few ms. All in `Update`, so it works in No UI Mode too.
- **Sequencer (`FillBuffers`).** The mixer mixes every clip overlapping this buffer's timeline span
  `[_audioPosSec, _audioPosSec + count·speed/rate)`; each clip's output-frame window `[o0,o1)` is
  derived from its overlap. A clip's file source is read **sequentially** and reseeked **only on a
  discontinuity** — `|clipStartSecThisBuffer − source.NextTimelineSec| > ReseekTol` (a fresh clip, or a
  hard-sync, the latter via `ForceReseekAll` setting every `NextTimelineSec = NaN`). Contiguous playback
  ⇒ no seeks, just decode-forward at the current `speed`. There is **no loop handling in the mixer** —
  the visual playhead already wraps, so `TargetSec` leaps and the hard-sync path repositions the audio.
  Past a clip's file end the reader returns 0 ⇒ silence.
- **Per-sample fades (not per-frame).** `GainAt(clip, timelineSec)` in the mixer reproduces
  `ClipData.GainAt` as a *smooth* linear ramp evaluated at every output sample (× `clip.Gain`), so
  audio fades don't zipper at frame boundaries. Same envelope the editor draws — reused *concept*,
  smooth *implementation*.
- **Per-audio-track loudness meter (`Track Levels` output, 2026-08-26).** The mixer meters each audio
  track's loudness and publishes it for the node's `Track Levels` `Spread<float>` (aligned by
  **audio-track** order — one entry per `TrackKind.Audio` track, standard tracks excluded, so it's an
  audio-domain output like `Audio`, NOT bundled in `Info`). Each `AudioClipSnap` carries its audio-track
  index (`AudioTransport.Build` tags it, `AudioTrackCount` = how many audio tracks); in the mix loop the
  clip's post-gain samples add into `_levelEnergy[track] += l²+r²`, so all clips on a track meter as one.
  Per buffer, `PublishLevels` turns energy into `RMS = √(energy / 2·count)` and applies **VU-style
  ballistics**: instant attack, exponential release over `LevelReleaseTau` (0.3 s) — a stopped/silent
  track releases to 0 (the idle early-return also calls `PublishLevels` with zero energy so meters keep
  falling). **Audio-thread hygiene:** the envelope/energy state is audio-thread-only and the published
  `float[] _levels` is **ping-ponged** between two preallocated backing arrays (swap the `volatile`
  reference), so a normal buffer allocates nothing; the main thread reads `Levels` and copies it into the
  spread (sized to the doc's audio-track count, so the width is stable even before the audio thread runs).
  Reallocated only when the audio-track count changes (a one-buffer meter reset, inaudible).
- **Threading model — one immutable snapshot, swapped by reference.** The node builds an
  `AudioTransport` (`Playing` / `TargetSec` / clip list / audio-track count) on the main thread each frame via
  `AudioTransport.Build(doc, fps, playing, playheadContinuousFrames)` and hands it over with
  `SetTransport` (a `volatile` reference — atomic). The **audio thread only ever reads that reference**,
  never the live `InterpoleDocument`, and it doesn't publish anything back (the playhead is the node's,
  not the mixer's). All geometry in the snapshot is in **seconds** (frames ÷ Fps), so the mixer is
  independent of the engine sample rate (read live via `SampleRate`, can change under us). *Caveat:* the
  snapshot is rebuilt **every frame** (small per-frame allocation) rather than only on `Changed` —
  simplest, and it keeps playing state + target live; revisit if GC churn ever matters.
- **Per-clip file source, opened off the audio thread, played through a live varispeed resampler.**
  `AudioClipSource` wraps NAudio `AudioFileReader` + a **`WdlResampler`** (NAudio.Dsp — the low-level
  resampler, *not* the fixed-rate `WdlResamplingSampleProvider` wrapper, because we need to change the
  ratio live for the chase). `ReadFrames(dst, count, speed, engineRate)` calls
  `SetRates(sourceRate, engineRate/speed)` each buffer then `ResamplePrepare` → read source →
  `ResampleOut` — exactly the wrapper's own Read loop, plus the live rate (speed = E/O ratio; `speed`× =
  that much file per output second). Opened on a `Task` so the audio thread never blocks on IO — until
  ready (or if missing) it reads silence (same "load then appear" as the waveform). Keyed in the mixer's
  pool by **clip id** (not path) so two clips off one file get independent positions; the pool is
  **audio-thread-owned** (`EnsureSource` creates lazily, `PruneSources` disposes any whose clip left the
  snapshot — no cross-thread lifetime races). `Seek` sets `reader.CurrentTime` + `resampler.Reset()`.
  *Caveat:* MP3/compressed decode on the audio thread; WAV/PCM (the focus) is cheap.
- **Same-rate sources BYPASS the resampler (crackle fix, 2026-08-25).** `ReadFrames` branches: when
  `sourceRate == engineRate` it copies the reader straight into `dst` (no `WdlResampler`); only a
  different-rate source goes through the resampler. **Why:** `WdlResampler` is *pathological at ratio ≈
  1.0* — driven at unity it injects periodic sample discontinuities, an audible constant "digital
  crackle." A same-rate file (which needs no conversion) hit exactly that, because the varispeed chase
  parks `speed` at 1.0 in the deadband → `SetRates(rate, rate/1.0)` → unity. A *different*-rate file
  (e.g. 44.1 k into a 48 k engine) sits at ratio ≈ 0.92 and is clean, which is why the bug looked
  backwards ("48 k crackles, 44.1 k fine") until the engine rate was measured. **The engine runs at the
  audio *device* rate (48 k here), not a fixed 44.1 k** — `SampleRate` is read live and can be anything;
  never assume. Bypassing plays the source bit-for-bit (verified: error = 16-bit rounding only). The
  bypass ignores `speed` (plays at natural 1.0); any varispeed correction still lands because the mixer
  advances the timeline by `speed`, so the file drifts vs the timeline and the existing declicked reseek
  absorbs it — no pitch-bend, no crackle. Diagnosed by instrumenting the live `FillBuffers` (buffer
  size/rate/call-timing/short-reads to a Desktop log) after the artifact proved impossible to reproduce
  offline; that log is what revealed engine rate = 48 k and pinned the unity-ratio resampler.
- **Waveform peaks are a separate read path.** `FileStreamSignal`/`AudioFileReaderVVVV` expose **no
  raw samples**, so `WaveformCache` decodes the file a second time (NAudio) into fixed-bucket min/max
  envelope pairs (256 source frames/bucket), **off-thread**, cached by `path + lastWriteTime`, shared
  across clips. **Not serialized** (would bloat the JSON) — recomputed on first draw after a reload.
  `DrawWaveform` maps each screen column to its file-time window (from `FileInPoint`, at natural rate)
  and draws the min/max line; columns past the file end stay blank; `loading...` / `! missing file`
  otherwise. `ProbeDurationSeconds` reads just the header (no full decode) to size a dropped clip.
- **Data model additions are back-compatible.** `TrackData.Kind` (`TrackKind.Standard=0` / `Audio`)
  and `ClipData.FilePath?` / `FileInPoint` (trim, seconds) / `Gain` — all default so pre-audio
  documents (field absent → `Standard`, `FilePath` null) load unchanged. `ClipData.IsAudio =>
  FilePath is not null` (`[JsonIgnore]`, like `End`). `Start`/`Length` stay integer frames; only the
  mixer works in samples/seconds.
- **Mute / Solo per audio track (2026-09-06).** `TrackData.Mute` / `TrackData.Solo` (both default false,
  back-compat; persisted + undoable — they ride the structural snapshot like any track field). Audibility
  is resolved **in `AudioTransport.Build`** (main thread), not the audio thread: a first pass sets
  `anySolo` (any audio track soloed), then each clip's `AudioClipSnap.Audible = !tr.Mute && (!anySolo ||
  tr.Solo)` — **solo isolates, mute gates on top, soloed+muted = silent**. The mixer just does
  `if (!clip.Audible) continue;` at the top of the clip loop, so an inaudible track is **not decoded** and
  **meters 0** on `Track Levels` (skipped before the meter accumulate) — i.e. the meter follows what you
  hear. Unmute is instant (next snapshot); the source reseeks on the discontinuity like any gap. UI: two
  small icon buttons in each **audio** track header (`TrackToggleRects`/`DrawTrackToggle`), right-aligned
  and level with the name (name clipped so it can't run under them): a hand-drawn **loudspeaker**
  (`DrawSpeakerIcon`) for mute and an **ear** (`DrawEarIcon`) for solo — drawn from draw-list primitives
  since no icon font is loaded; the **state** is the fill colour (red = muted, yellow = soloed), the glyph
  itself doesn't change. Standard tracks have none. The header hit-test takes a plain left-click on the
  buttons **before** the rename/reorder/delete gestures (audio tracks only, `!alt`).
- **Per-track volume (0..1).** `TrackData.Volume` (default 1, back-compat + undoable like Mute/Solo).
  Folded into each clip's gain **in `AudioTransport.Build`** — `c.Gain * Clamp(tr.Volume, 0, 1)` — so the
  mixer stays clip-only and the per-track VU (`Track Levels`) is automatically **post-volume** (it meters
  what the mixer plays). UI: a **real ImGui `DragFloat`** in each audio track header, just left of the M
  button (`TrackVolumeRect` / `DrawTrackVolume`), so it drags/type-edits like every other ImGui value
  (not a hand-drawn fader). It sits over the full-canvas `InvisibleButton` via that button's
  `SetNextItemAllowOverlap` (same trick as the inspector), so it wins hover and the manual header
  hit-tests (which only run while the canvas itself is hovered) don't fight it. Live-previews into the
  track (audio responds mid-drag, since the transport rebuilds from the live document each frame) and
  `Commit`s once on release for a single undo step + `Data`-pin persist — the inspector's edit convention.
- **Left-edge resize head-trims audio clips.** Dragging a clip's left edge advances `FileInPoint` by
  the same delta (clamped ≥ 0), so the audio content stays put on the timeline (proper NLE trim), and
  the drawn waveform shifts to match. In-point + gain are also editable in the inspector's **AUDIO**
  section. Copy/paste and `CloneClip` carry `FilePath`/`FileInPoint`/`Gain`.
- **File dialog = Win32 `GetOpenFileNameW` on an STA thread** (`NativeFileDialog`), not WinForms — one
  P/Invoke, no `UseWindowsForms`. Runs on its own STA thread (the ImGui/Skia render thread isn't STA)
  and **blocks that frame** while open (fine for a modal picker; could be made async later). Fired from
  the audio-track branch of the empty-lane double-click (`AddAudioClip`); the clip is sized to the file
  duration, clamped to the gap.
- **Mixer creation is fault-tolerant.** `new InterpoleMixerSignal()` touches VL.Audio's global engine
  (which may open an audio device); the node creates it in a `try/catch` (`TryCreateMixer`) and holds
  it as **nullable**. If it fails (no audio device on the machine), the node is a full timeline with
  audio simply disabled (empty `Audio` output, wall-clock playback) — dropping the node can't break
  for existing non-audio users.

## Editor geometry (how the bands are laid out)

Everything is computed each frame from `_origin` + these constants:

```
TransportHeight   transport toolbar (top, full width)
RulerHeight       frame ruler
LoopStripHeight   loop-range ribbon, carved from the TOP of the ruler
MarkerLaneHeight  marker strip, between the ruler and the track lanes
HeaderWidth       track-name column (left)
TrackHeight       one lane

_rulerTop    = _origin.Y + TransportHeight
_loopBottom  = _rulerTop + LoopStripHeight    (loop strip = [_rulerTop, _loopBottom); scrub/labels below)
_rulerBottom = _rulerTop + RulerHeight        (ruler ends here; scrub band = [_loopBottom, _rulerBottom))
_lanesTop    = _rulerBottom + MarkerLaneHeight (marker lane = [_rulerBottom, _lanesTop); tracks below)
_laneLeft    = _origin.X + HeaderWidth
```

The marker lane was slotted in by keeping `_lanesTop` meaning "top of the track lanes" (so all the
track / clip math is untouched) and introducing `_rulerBottom` = the *old* `_lanesTop` for the ruler's
own bottom. The lane simply pushes the tracks down by `MarkerLaneHeight`; `ClampScroll`'s view height
subtracts it too. Cheapest possible insertion — the only track-side change was the `_lanesTop` formula.

Time transform: `FrameToX(f) = _laneLeft + f*_pxPerFrame - _scrollX`; inverse `XToFrame`.
Vertical: `TrackTop(i) = _lanesTop + i*TrackHeight - _scrollY`. Each region draws inside its
own `dl.PushClipRect(...)` so nothing bleeds across the dividers. Interaction is fully manual
hit-testing over one big `InvisibleButton`; the only real ImGui widget used is the `InputText`
overlay for inline rename (same trick as Automata — `HandleInteraction` is skipped while an
inline edit is open).

## Markers (2026-08-19)

Named points on the frame axis in a **global** lane below the ruler (not per-track). `MarkerData`
(`Id`/`Name`/`Frame`/`Stop`) lives in `InterpoleDocument.Markers` — default empty and absent from
old JSON, so back-compat is free. Decisions worth remembering:

- **Creation asks first.** Unlike clips/tracks (inline rename *after* placing), a marker needs a
  name **and** a "stop playback" checkbox up front, so it uses the codebase's **first real ImGui
  modal** (`BeginPopupModal`, `DrawMarkerDialog`) instead of the `InputText` overlay. Nothing is
  added until **OK**; the same modal edits an existing marker (double-click). It also carries a
  **colour** row (`MarkerData.Color`, an index into the shared `InterpolePalette`, drawn as ImGui
  `ColorButton`s) so markers recolour like clips. `_markerDialogOpen`
  gates `HandleInteraction`/`HandleShortcuts` the same way `_editKind` does for inline rename.
  Gotcha: ImGui.NET has **no `BeginPopupModal(string, flags)` overload** — must pass `ref bool` (we
  feed a throwaway `keepOpen`; the title-bar X just flips it and `IsPopupOpen` closes us next frame).
- **Edit gestures mirror clips** (the user's call): drag to move (Shift-snaps via the shared
  `Snap`/`CollectClipEdgeTargets`), double-click to edit, Alt/Middle-click to delete. New
  `DragMode.MoveMarker`, intercepted before the clip-drag catch-all.
- **Inspector editing.** Clicking a marker also selects it (`_selectedMarkerId`) — **mutually
  exclusive** with a clip selection (each side clears the other) so the one panel edits exactly one
  thing. `InspectorContents` checks the marker first and hands off to `MarkerInspector` (name / colour
  / frame / on-reach action / delete), reusing the clip inspector's commit conventions and shared name
  buffer. The on-reach action is **Stop or Jump-to-marker, mutually exclusive** (see below); both the
  inspector and the create/edit modal enforce it (setting one clears the other's buffer/field).
- **Lane height = `TrackHeight`** (the user wanted it to match a track row). The pin itself is a fixed
  `MarkerPinHeight` centred in the lane, so it stays a compact Unity-style pin instead of stretching.
- **Undo covers markers.** `StructuralSnapshot` and `ApplySnapshot` now include `Markers`, so
  add/move/edit/delete are undoable — matching clips. (The playhead/loop stay out of history as before.)
- **Action-on-marker** is in `AdvancePlayback`, checked *before* the wrap/end logic:
  `Document.EarliestActionMarkerIn(from, scanEnd)` finds the earliest **action** marker (Stop *or*
  Jump) strictly after the current frame and ≤ the frame we'd reach this step. Strictly-after means you
  can **resume off / land back onto** an action marker without it re-firing the same frame. When
  looping, `scanEnd` is clamped to the **loop-out** (it wraps at `lo`, so markers past it aren't crossed
  on this pass).
  - **Stop** (or a jump whose target was deleted): park on the frame + `_playing=false` + `Changed`
    (persist like Stop). Drawn with a **bright outline** (the pin fill is the user-chosen palette colour,
    so the action can't be the fill). A stop marker inside the loop region halts every pass — the user
    picked plain "halt on the frame", not a loop-override.
  - **Jump** (`MarkerData.JumpTargetId`, another marker's **Id** so it follows moves/renames): seek to
    `Document.FrameOfMarker(target)` and keep playing. A **backward** target loops **forever** by design
    — the only exits are the other transport paths that reseat the playhead (Stop marker, `Active State`
    change, `Set`/`Jump` pins, Rewind), exactly as the user specified; no loop-count, no dedicated pin.
    `_playAccum` is reset on the jump so this frame's fractional overshoot isn't carried across. Drawn
    with a **chevron** pointing the seek direction (left = loop-back, right = forward skip).
  - Stop and Jump are **mutually exclusive** per marker (`MarkerData.HasAction = Stop || JumpTargetId`);
    the editor clears one when you set the other, and `RemoveMarker` clears any `JumpTargetId`
    referencing the deleted marker so no jump dangles. Old docs (no `JumpTargetId`) load unchanged.
- **`Active Marker` output (2026-08-26).** A `string` out pin reporting the name of the marker the
  playhead is **on** this frame, `""` when on none. Its point is stop markers: when `AdvancePlayback`
  parks on one, `Document.Playhead == marker.Frame`, so the output naturally holds *which* marker halted
  playback for as long as it stays parked — the patch reads the stop's name with no extra state. The
  value is computed on demand in `InterpoleEditorWidget.ActiveMarkerName` (resolved from the live
  document, so a rename shows through): first any marker whose `Frame == Playhead` (covers a stop-parked,
  a scrub-onto, or a landed-on marker), else — **only while playing** — `_crossedMarkerName`. That field
  catches a **plain marker skipped by a multi-frame advance**: `AdvancePlayback` resets it each step and,
  when no action marker fired, sets it to the last plain marker in `(prev, scanEnd]` (largest frame), so a
  non-stop marker still pulses active for the one frame it's crossed even if the playhead never lands on
  its exact frame. Gating on `_playing` means a stale crossed name can't leak while parked. Also added to
  `InterpoleInfo` + unpacked by `GetTransport` (transport scalars). Kept out of the model/undo — pure
  derived output, like `Is Playing`.
- **`Marker` enum + `Jump` pin.** Markers populate a **dynamic enum** (`InterpoleMarker` /
  `InterpoleMarkerDefinition`, the VL.HDE template shape) synced from the document every node
  `Update` via `InterpoleMarkerDefinition.Sync(name→frame)` (only pulses its change-trigger when the
  name set actually differs). A rising-edge `Jump` resolves the selected name **from the document**
  (not the enum tag) → `SeekTo` + `Play`. **Caveat: the enum definition is an app-global singleton**
  (vvvv's model), so multiple Interpole nodes share one dropdown reflecting whichever synced last —
  fine for the single-timeline case, documented as a limitation. Jump reuses `Play()`, so with Loop
  on and the marker outside the loop region, `OnTransport(Play)` snaps to loop-in first (edge case).

## Following an Automata state (2026-08-20)

The "where it all comes together" step: an `Active State` **string** input on the node. Each Update, if
it **changed** since last frame and its value matches a marker name, the node `SeekTo`s that marker and
`Play`s — an Automata state activating drives the Interpole playhead. Change-detection (`_prevActiveState`,
starts `""`) makes it a one-shot per state change; re-entering a state (A→B→A) fires again because the
string changed; exact name match; no match / empty → nothing. It's independent of the `Marker`/`Jump`
pins (they can coexist).

**Decoupling decision (the user's call):** kept a plain `string`, NOT an `AutomataInfo` input. A direct
`AutomataInfo` pin would force VL.Interpole to hard-reference VL.Automata (they live in separate repos —
`Documents/INTERPOLE` vs `Documents/AutomataX`), so Interpole couldn't build/load without it. The string
delivers identical behaviour with zero coupling: in the patch you wire `Automata → GetActiveState →
Interpole.Active State`, and any other string source works too. If a tighter one-wire integration is ever
wanted, the clean route is a shared contract assembly both reference — not a direct cross-repo reference.

## One-cable `Info` output (2026-08-19)

`InterpoleInfo` (`Engine/InterpoleInfo.cs`) is an immutable `record` bundling the node's value outputs
(Current Frame, Is Playing, Active Marker, the per-track **track names**, and the per-track spreads) so a
patch can route the whole timeline state on
one wire — a direct port of VL.Automata's `AutomataInfo`. It's a **snapshot**, not a live handle:
`Update` builds a fresh record each frame from the values it already computed (`EvaluateOutputs`), so
scalars are copied and the spreads are shared **by reference** (they're immutable) — one small record
per frame, no list copying. Splitter getters (`GetTransport` / `GetActiveClips` / `GetUpcomingClips` /
`GetTrack` / `GetActiveMarker` / `GetKeyframes`, zero-field `[ProcessNode]`s that tolerate a null input)
unpack it downstream, same convention as Automata's `GetActiveState` etc. **`Audio` is deliberately excluded** — it's a live `Spread<AudioSignal>`
bus wired straight into VL.Audio, not information to route/unpack, so it keeps its own pin.

- **`TrackNames` (2026-09-02).** The bundle carries a `Spread<string> TrackNames`, aligned by track order
  like every other per-track spread (index *i* is the same track across all of them). Built in
  `EvaluateOutputs` from each `TrackData.Name`. Its purpose is to let a splitter resolve a track **by name**
  from the snapshot alone (the snapshot has no other track identity) — used by `GetTrack`. Added to the
  `InterpoleInfo` ctor + `Empty`; back-compatible (the existing splitters read named properties, so the new
  positional field only touched the single construction site in `InterpoleNode`).
- **`GetTrack` single-track split (2026-09-02).** Picks **one track by name** and unpacks its active-clip
  values as **scalars** — `Active Clip Name` / `Clip Frame` / `Clip Length` / `Clip Progress` / `Fade Gain` /
  `Active Color` — the per-track equivalent of `GetActiveClips` for a single named track (the user's ask).
  `Track` (a plain `string`, chosen over a dynamic-enum dropdown to avoid the app-global-singleton enum
  machinery) is matched **exactly** against `Info.TrackNames`, first match wins. A null `Info`, an
  empty/unknown `Track`, or a track sitting in a gap all resolve to the same "nothing" values
  (`""` / `-1` / `-1` / `0` / `0` / transparent), so `Clip Length == -1` reads as "no active clip / no such
  track". Shares the safe-index (`At<T>`) + null-tolerant conventions of the other Get… nodes.

## Keyframe editor — per-clip float tracks (2026-09-05)

Any clip can hold **float tracks** — named parameters animated with **keyframes** (After-Effects
style) — edited on a **per-clip page**. Built on the **`keyframe-editor`** branch (two-branch workflow:
small fixes land on `master`, this big feature stays isolated until merged), in gamma-verified baby
steps. **The shipped design diverged from the original plan** (which called for a clip-*local* ruler
and multi-lane tracks): what's built is simpler and reuses the timeline wholesale.

- **The clip page IS the timeline view.** Same time axis (`_pxPerFrame` / `_scrollX`), ruler, loop
  ribbon, marker lane, playhead and transport — only the **header column + lanes** swap to the open
  clip's float tracks. So the same moment in time / zoom / markers are visible; only the content is
  "inside the clip". `Draw()` branches on `_mode` (`Timeline` | `Clip`): the clip branch calls the
  shared chrome (`DrawRuler` / `DrawLoopBar` / `DrawMarkerLane` / `DrawPlayhead` / `DrawSnapGuide` /
  transport) unchanged and swaps `DrawLanes`/`DrawHeaders`/`DrawCorner` for `DrawFloatLanes` /
  `DrawFloatHeaders` / `DrawFloatCorner` (colour/metric twins). Only the active view draws — no double
  cost. The clip's own `[Start,End)` span is tinted in its colour and **the timeline outside it is
  dimmed** (a dark overlay) for orientation.
- **Interaction is shared + mode-gated.** `HandleInteraction` / `HandleShortcuts` run in both modes;
  the chrome branches (pan/zoom/transport/loop/ruler/markers) are common, and the timeline's
  clip/track/lane branches are skipped in clip mode (`if (_mode==Clip) { HandleClipLaneInteraction(); 
  return; }`). Delete / Ctrl+C / Ctrl+V are mode-aware: clips on the timeline, keyframes on the clip
  page. This keeps the battle-tested timeline path untouched.
- **Data model (free undo + persistence).** `ClipData.FloatTracks : List<FloatTrackData>` and
  `FloatTrackData.Keys : List<Keyframe>` (`Engine/InterpoleData.cs`), both default-empty → back-compat
  and **undo + `Data`-pin autosave come for free** because `StructuralSnapshot` serialises `Tracks`
  wholesale (exactly how Markers was added). `Keyframe{ Id, Frame, Value }` — **`Frame` is clip-local**
  (0 = clip start), so keyframes follow the clip when it moves. `Id` (backfilled by the editor if
  empty) exists only so the editor can hold a selection across undo/redo. `SampleAt(localFrame)` =
  linear between bracketing keys, holds the ends, constant `Value` when no keys; `SetKeyframe` creates
  or updates the key at a frame (keeps `Keys` sorted).
- **Create/edit = the value field at the playhead.** The header's draggable value shows
  `SampleAt(playhead - clip.Start)`; editing it calls `SetKeyframe` — a new key at the playhead, or an
  update to the one there (the field goes **amber** when the playhead is on a key). Gated to the clip's
  span (`BeginDisabled` outside it — the dimmed region).
  - **`K` button = add a key without changing the value.** A small M/S-style button just left of the
    value field (`FloatKeyButtonRect` / `DrawKeyButton`, hit-tested in `HandleClipLaneInteraction` before
    the rename/reorder/delete gestures). It calls `SetKeyframe(local, SampleAt(local))`, so it drops a key
    at the playhead **on** the current curve (value unchanged). Enabled only `within && !onKey` — off the
    clip or already on a key it reads dim and does nothing (no keys stacked on the same frame). A click on
    it is always swallowed, so it never starts a header reorder drag. Undoable/persisted via `Commit`.
- **Keyframe selection / move / copy-paste** reuse the timeline's own infra. `_kfSelection`
  (HashSet of ids, pruned each frame to the active clip), marquee via the shared `MarqueeRect` /
  `DrawMarquee` (new `KfMarquee` drag mode), move via a new `MoveKeyframe` drag that shifts the whole
  selection by a delta and re-sorts `Keys`, Shift-snapping the grabbed key through the shared
  `Snap`/`_snapTargets` (clip edges + playhead + *other* keys) with the same cyan `DrawSnapGuide`.
  Copy buffer is `(trackIndex, frame, value)`; paste offsets so the earliest key lands on the playhead,
  into the same track index. Playhead Shift-scrub also adds the active clip's keys to the snap targets.
- **Bottom tab bar.** `[Timeline]` + one tab per opened clip (`_openTabs` ordered list, `_activeClipId`
  = shown clip), each a colour diamond + name + ✕. Hand-drawn on the draw list with **bottom-rounded
  corners** (`ImDrawFlags.RoundCornersBottom`) so they read as hanging from the content (a top-rounded
  ImGui tab would look upside-down at the bottom). `TabBarHeight` carves the strip out of the bottom
  (`LanesBottom = CanvasBottom - TabBarHeight`). **Audio
  clips are excluded** (no keyframes yet): `OpenClipTab` skips `IsAudio` and the inspector's *Edit
  Keyframes* button is hidden for them.
- **Opening a page = double-click, split by where you land (2026-09-05).** A double-click on a clip
  splits on the label: **on the label text** it edits the clip (inline rename + colour, `StartEditClipLabel`);
  **anywhere else on the clip body** it opens that clip's keyframe page (`OpenClipKeyframes`). `HitClipLabel`
  recomputes the drawn label's screen rect (same geometry as `DrawClip` — the left band from the clip's
  left edge through the end of the text, full clip-body height) so the split matches what's on screen; a
  clip whose name fills it therefore mostly edits (the inspector *Edit Keyframes* button is the fallback).
  Audio clips have no keyframes, so they always edit. `OpenClipKeyframes` is the single entry point
  (select + `OpenClipTab` + switch to `EditorMode.Clip`), shared by the double-click and the inspector
  button. **The old `K`+click gesture was removed** in favour of this. **Escape** on the keyframe page
  drops back to the Timeline (the clip's tab stays open), gated in `HandleShortcuts` (so a focused float
  field swallows it first).
- **Header column is wider on the clip page** (`ClipHeaderWidth` 340 vs the timeline's `HeaderWidth`
  240) for track names; `_laneLeft` picks the width by mode, so all the shared chrome shifts with it.
  The value field is the only live ImGui widget in the header (overlaid via the InvisibleButton's
  `SetNextItemAllowOverlap`); its width is fixed (96px), the name takes the slack (`FloatHeaderNameWidth`).
- **Float-track headers mirror the timeline's track headers (2026-09-05).** The **name is plain text**
  (hand-drawn, not an always-on input), so the header behaves exactly like a timeline track:
  **double-click a name → inline rename** (new `EditKind.FloatTrackName`, reusing the shared
  `DrawInlineEdit`/`CommitEdit` overlay — positioned in the active clip's header column, so `editClip`
  stays null and no colour swatches show); **drag a header → reorder** (`DragMode.MoveFloatTrack` +
  `ContinueFloatTrackDrag`, the clip-page twin of `MoveTrack`/`ContinueTrackDrag` operating on
  `clip.FloatTracks`; blue row-tint + insertion lines via `DrawFloatTrackDragFeedback`, only swapping
  once the cursor crosses a row edge); **Alt/Middle-click → delete**. All of it lives in
  `HandleClipLaneInteraction`'s new header branch (`mouse.X < _laneLeft`), reached only over the
  plain-text name — the value `DragFloat` sits on top so its clicks make the canvas `!hovered` and
  return before the header branch. Uniform rows from `_lanesTop` (`FloatRowTop`/`FloatTrackIndexAtY`,
  no vertical scroll yet, like before). The old always-on name `InputText` was removed.
- **Gotcha — ImGui button height under 2× DPI.** The corner *+ Float Track* button was clipped to its
  top half: a fixed pixel height is shorter than the font at 200% scaling. Fixed by giving it the
  timeline corner-button rect (`RulerHeight-8`, taller than the font) — ImGui centres the label
  vertically, so it never clips. (Auto-height `Vector2(w,0)` also works.)
- **Output — `GetKeyframes`.** `InterpoleInfo` gained a `Spread<ClipKeyframeSnapshot> ClipKeyframes`
  (`ClipKeyframeSnapshot{ ClipName, TrackNames, TrackValues, Active }`), built each frame in
  `InterpoleNode.BuildClipKeyframes` for every clip **with** float tracks (track-then-clip order),
  each track's value sampled at the playhead (`Active = clip.Contains(playhead)`). The `GetKeyframes`
  node (Info + a clip **name**) is **playhead-aware**: it returns the same-named clip that is **active
  under the playhead** (so a *copy* of a clip plays its own animation, not just the first clip in
  document order); first wins among several active at once; when **none** is active it falls back to the
  first same-named clip's held value. A pure splitter like the other Get… nodes; kept out of the
  per-track spreads (it's clip-keyed, not track-keyed).
- **Copy/paste carries float tracks (2026-09-06).** `CloneClip` deep-copies `ClipData.FloatTracks` via
  `CloneFloatTracks` (fresh ids for every track + keyframe, preserving `Name`/`Value`/`Frame`/`Interp`),
  and the paste construction clones again — so a pasted clip carries its own **independent** keyframe
  animation with no shared ids (which would confuse the editor's keyframe selection). Was previously
  dropped: `CloneClip`/paste only copied the audio + fade + colour fields.
- **Easing / interpolation types (2026-09-06).** Each keyframe carries an `InterpMode`
  (`Engine/InterpoleData.cs`): `Linear`(=0, back-compat) / `Step` / `EaseIn` / `EaseOut` / `EasyEase` /
  `AutoBezier`. **After-Effects per-side model:** a segment A→B is shaped by **A's outgoing side + B's
  incoming side**, each mode being a preset (in,out) pair (Linear = both linear, EaseIn = eased in +
  linear out, EaseOut = linear in + eased out, EasyEase = both eased, AutoBezier = both auto, Step =
  out-hold). `SampleAt` is now a **cubic Hermite**: linear side → tangent = the straight A→B slope
  (linear+linear collapses to the exact old line); eased side → tangent = **0** (zero velocity, fixed
  strength, no handles/influence); auto side → `AutoSlope` = central-difference (Catmull-Rom) through the
  neighbours, one-sided at the ends; Step returns A's value and jumps at B. Set via a **right-click
  context menu** on a keyframe (`DrawKeyframeMenu`, non-modal `BeginPopup`, gated by `_kfMenuOpen` like
  the marker dialog): the intercept sits **before the pan trigger** in `HandleInteraction`, so a
  right-click *on a key* opens the menu while right-drag on empty lane still pans. Selection rule: a key
  already selected → menu edits the whole `_kfSelection`; a key not selected → it becomes the sole
  selection first (`ApplyInterpToSelection`, one undo step; `CommonSelectedInterp` drives the check mark,
  null when mixed). **Icons encode the type** (`DrawKeyframeMarker`, renamed from `DrawKeyframeDiamond`):
  diamond / square / ◄ / ► / hourglass / circle, same amber fill + highlight/ring per shape. Right-click
  only (no F9-style shortcuts); menu holds only the six types (no delete/copy). `GetKeyframes` output is
  unchanged (only the sampling maths got richer).
- **Not built (vs the old plan):** keyframe **lanes** (several typed value lanes per track), vec2/3/4 +
  Colour lane types, editable **bézier handles / adjustable ease influence** (the six presets are fixed),
  and the opaque `Keyframes` cable + typed `GetKeyframeFloat/Vector*/Color` getters. The current output
  is one float per track. **No vertical scroll** for float rows yet (rows past the bottom are
  clamped/skipped) and **the tab strip doesn't scroll** (tabs stop when out of width).

## Rendering performance (2026-08-20)

The editor is immediate-mode: every frame `InterpoleNode.Update` calls `_toSkia.Update(...)`, which
re-runs the whole `Draw()` pass and re-tessellates it (No UI Mode skips exactly this). There's no
partial cache *within* a frame.

### Snapshot cache for playback (done, 2026-09-10) — `Editor/SnapshotLayer.cs`

The dominant cost was **per-pixel rasterization**: ToSkiaLayer re-draws the whole opaque editor onto
the window every frame (thousands of DrawVertices), so with a large Interpole window the cost scales
with window size and **stuttered vvvv's realtime graphics while playing**. Fixed by caching: while
**playing**, almost nothing on screen changes except the playhead + timecode, so we blit a cached
still of the static UI (one quad) and let the editor draw only the moving overlay on top. Stutter
gone; verified in gamma at 175% DPI.

- **`SnapshotLayer : ILayer`** sits between `SetSpaceUpstream2(DIPTopLeft)` and `ToSkiaLayer`
  (`_editorSpace.Update(new SnapshotLayer(_toSkia), ...)`). The node drives its `Mode` + `CurrentKey`
  each frame: `Live` (stopped → pass-through), `Capture` (render static + record into the cache),
  `BlitOverlay` (blit the cached page + draw the overlay on top).
- **Widget `PlayCache` passes** (`PlayPass`, set by the node): `Static` draws the full editor minus
  playhead/snap-guide/readout (captured); `Overlay` draws only the playhead + `DrawReadout` timecode
  (over the blit, window forced `NoBackground`). Editing is locked in play — `HandlePlayShortcuts`/
  `HandlePlayInteraction` allow only Space, transport, zoom and playhead scrub.
- **Per-page LRU** keyed `(page, zoom, size)` (`PageKey`, cap 8). The node **page-aligns** the scroll
  to the playhead's page (`SetPlayScroll`, skipping the edge-nudge `EnsurePlayheadVisible` in play), so
  pages recur deterministically and a **loop re-hits cached pages every lap** (no re-capture). First
  visit of a page (or a new zoom) captures it — a brief 1-frame static (no playhead) then smooth.
- **Capture = SKPicture record → rasterize** (ToSkiaLayer draws only to `caller.Canvas`, so a recording
  canvas captures every op). **Coordinate gotcha:** the DIPTopLeft `Transformation` scale is
  `DIPFactor*100` (175 at 175%); the net content matrix is `Transformation·Scale(Scaling/ScaleX)` — use
  that or the surface is sized `vb*175` (~44 gigapixels → null → NRE). The offscreen surface must carry
  the **destination color space** (else the blit darkens), and the Overlay pass must set ImGui
  `NoBackground` (else its dark WindowBg paints over the cache). Full write-up in the
  `vl-imgui-skia-layer-snapshot-cache` memory.

Other levers:

- **Idle-skip rendering (not done).** Skip the ImGui frame when *stopped* and nothing changed (no
  drag/edit/dialog/hover/resize). The snapshot cache already handles the *playing* case (the one that
  stuttered); idle-skip would additionally save the stopped-but-idle CPU. Needs "wake on input" gating.
- **Waveform envelope cache (done).** `DrawWaveform` used to recompute a per-pixel min/max envelope
  every frame (each column summing peak buckets — costly zoomed out on long files). Now `GetWaveEnvelope`
  caches the per-column min/max per clip (`_waveEnv`, keyed by clip Id), rebuilt only when peaks /
  FileInPoint / pxPerFrame / Fps / length change; drawing indexes it by **clip-relative pixel** so
  scrolling never rebuilds it. Capped at `WaveEnvMaxColumns` (100k) — a very long clip zoomed in falls
  back to direct drawing, which is cheap in that regime (≈one bucket per column). Cache entries are
  dropped on clip delete. Output is pixel-identical to before.

**Texture prerender — investigated, NOT viable here.** The obvious idea (render the waveform once to an
`ImTextureID` and `dl.AddImage` one quad) doesn't work in this backend: an `ImTextureID` must be minted
by the renderer, and VL.ImGui.Skia's `SkiaContext` (which owns `AddLayer`/`TextureId`) is **`internal`**
— confirmed by compiling a probe against the pack (`SkiaContext.AddLayer(SkiaWidget, Vector2, Vector2)`,
inaccessible). The only public route is the retained `SkiaWidget` (set `.Layer` to an `ILayer`), which
clashes with our all-imperative draw-list editor. Don't retry the texture route without either an
internal API opening up or a widget-tree rewrite.

## Known issues / rough edges

- **DPI: the editor renders 2× on 200% Windows scaling** (shared with Automata — see the
  `vl-automata-editor-dpi-pixel-lock` memory). This is why several sizing constants (ruler
  height especially) were bumped by hand. A proper fix is to fold `1/DIPFactor` into the editor
  scale rather than chasing pixel constants — **not done yet**.
- Playback is real-time by default (dt × Fps), driven from the node's `Update` via `Tick` so it runs in
  No UI Mode; per-`Update` stepping is opt-in via the hidden `Frame Step Playback` pin (see timebase
  decision above). Real-time relies on the wall-clock `dt` being sane — a very long stall (or the node
  not being evaluated for a while) produces one large catch-up jump (bounded on loop).
- **Pan/zoom is persisted** — it survives a reload *and* a full vvvv restart. The view transform
  (time-axis zoom + horizontal/vertical scroll) lives on the document as `ViewPxPerFrame` /
  `ViewScrollX` / `ViewScrollY`, so it serializes onto the `Data` pin exactly like `Playhead` and the
  loop region. The widget **restores** it from the document in its constructor (clamped/floored; the
  first `Draw`'s `ClampScroll` re-fits the scroll to the content, so a smaller reloaded timeline can't
  strand the view), and **persists** it via `PersistViewIfChanged` — called once per interactive frame,
  it folds the current view back into the document and fires `Changed` only when the view has *changed*
  **and is at rest** (not playing, no drag/pan in progress). That "at rest" guard is what keeps a pan
  from writing the pin every frame during the drag (it saves once on release) and keeps the transient
  per-page scroll the node drives while playing (`SetPlayScroll`) out of the saved view — the parked
  view is what's kept. The model's `ViewPxPerFrame` default (6) mirrors `DefaultPxPerFrame`; a missing
  JSON field keeps that default, so pre-view documents load at the default view unchanged.
  - Undo/redo is unaffected: `StructuralSnapshot` builds a fresh doc with only `Title`/`Tracks`/
    `Markers` (view fields sit at their defaults, so a pan/zoom never makes a spurious undo step) and
    `ApplySnapshot` restores only those three, so undo/redo never moves the view.
  - Belt-and-suspenders for an *in-session* rebuild (external `Data` reload / paste): `LoadIfChanged`
    also reads the old widget's `View` (`ViewTransform`) and re-applies it after building the new
    widget, so a view change that hadn't been persisted yet isn't lost across the rebuild. In steady
    state this equals the document-restored view. Matches Automata carrying pan/zoom over a rebuild.
- **Audio caveats (all noted inline in the Audio section):** the transport snapshot rebuilds every
  frame (small alloc); MP3/compressed decode on the audio thread (WAV is cheap); WAV **paths are
  absolute** in the patch (a moved patch/machine breaks references — relative-to-patch is a future
  nice-to-have); the file dialog is modal and briefly freezes the editor frame; a seek rebuilds the
  WDL resample chain (tiny alloc, only on discontinuities). None block v1; all are M5/future candidates.
- **Audio playback is unverified on-screen as of this writing** — it builds clean (0 warnings) but the
  audio path (engine pulling, device output, sync-holds-over-time, fades/loop by ear) needs a gamma
  session with an audio device wired to VL.Audio's engine/output. That is the immediate next step.
- **README doesn't yet document the audio surface** (`+ Audio` track, audio clips, the `Audio`
  output pin) — update it once the above is verified, so usage docs don't claim unverified behaviour.
- **The playhead/timecode readout is drawn at the base font size** (`ReadoutTextScale = 1`). It was
  `2.4×`, but the node loads no font (`fonts: Spread<FontConfig?>.Empty`), so ImGui uses its default
  **bitmap atlas** font (ProggyClean, baked ~13px) — magnifying that looked pixelated. A crisp
  *large* readout needs a dedicated bigger font baked into the atlas via a `FontConfig` on the node's
  `ToSkiaLayer.Update` (and drawing the readout with `dl.AddText(thatFont, thatFont.FontSize, …)`
  instead of scaling) — **not done**; kept small for now.

## Next steps

1. **Verify audio in gamma** (M5 gate): wire the `Audio` `Spread<AudioSignal>` into VL.Audio's
   engine/output, confirm sound, that the playhead/waveform stay locked to the audio over a long clip
   (no drift), that fades ramp and the loop region wraps seamlessly by ear, and that No UI Mode plays
   headless. Then finish M5: **update README** with the audio surface; decide which audio caveats to
   address (async file dialog; rebuild the transport snapshot only on `Changed`; relative WAV paths).
2. **On-screen verification in gamma** of all the non-audio interactions (ongoing).
3. **DPI pixel-lock** (fold `1/DIPFactor` into the editor scale).
4. Audio nice-to-haves: **slip-edit** (drag inside an audio clip to move `FileInPoint` without moving
   the clip); per-clip **waveform colour/scale**; a master **gain/meter**; embedding or
   relative-path WAV references; scrub-audio preview while paused.
5. Nice-to-haves: ripple/push on cross-track drop (today it's strict exact-fit); a crisp large
   readout font (see Known issues). (Done: carry pan/zoom across an external reload; shift-snapping;
   marquee multi-select; multi-clip copy/paste; group drag; cross-track drag; loop region + ribbon +
   `Loop In`/`Loop Out` pins; playhead **seek from the patch** via `Time` + rising-edge `Set`;
   playback **outputs** — `Current Frame` + per-track `Active Clip Name` / `Clip Frame` /
   `Clip Length` / `Fade Gain` / `Active Color` — and the real-time `dt × Fps` timebase with the
   legacy mode behind `Frame Step Playback`; **audio tracks** — waveform authoring + a sample-accurate
   stereo mixer with audio-master sync, built but pending gamma verification.)

## Build

```
dotnet build "VL.Interpole/VL.Interpole.csproj" -c Debug
```

Clean build = 0 warnings, 0 errors. `[ProcessNode]` hot-reloads in gamma on rebuild.
