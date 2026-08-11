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
  the live `data` pin next frame — `LoadIfChanged` guards against that (via `_persistedData`)
  so our own autosave doesn't rebuild the editor and reset the view.

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
- **Playback timebase = real elapsed time (default), render-frames (legacy, hidden).** By default
  `AdvancePlayback(dt)` advances by `dt × Fps` (ImGui `io.DeltaTime`), accumulating fractional
  frames in `_playAccum` so one wall-clock second covers `Fps` frames regardless of render rate —
  so `Fps` is now the timeline's real frame rate, driving **both** playback and the timecode
  display (they finally agree). The old *one-frame-per-rendered-Draw* behaviour is kept behind the
  hidden **`Frame Step Playback`** pin (`FrameStepPlayback`) for cases that want it; there `Fps`
  affects the display only. `_playAccum` is reset on Play/Stop/Rewind so a new run never inherits a
  stale fraction. This is an *internal* timebase; an *external* `Time` input (deterministic, better
  for export) is still Next steps #2. Loop wraps with `np % end`, carrying any multi-frame overshoot.
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
  (the document still exists; only the ImGui frame is skipped) so a headless/exported patch still
  gets outputs. Fed off the internal playhead for now — an external `Time` input is Next steps #2.
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
- **Transport input pins: Play/Stop/Rewind are rising-edge, Loop is two-way state.** `Play`/`Stop`/
  `Rewind` are plain `bool` inputs; the node keeps `_prevPlay`/etc. and calls the editor once on each
  false→true transition, so a bang **or** a held/toggled bool behaves like one button press. The
  editor exposes thin `Play()`/`Stop()`/`Rewind()` wrappers over the private `OnTransport(TButton)` —
  the same path the on-screen buttons and Space use, so pin and UI share one implementation
  (restart-from-end, save-on-stop, playhead-0 on rewind come for free). These run **before** the
  editor update so a Play/Stop set this frame is reflected in this frame's draw (in No UI Mode the
  state is still set, but playback only advances inside the ImGui frame, which No UI Mode skips —
  pre-existing).
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

## Editor geometry (how the bands are laid out)

Everything is computed each frame from `_origin` + these constants:

```
TransportHeight   transport toolbar (top, full width)
RulerHeight       frame ruler
LoopStripHeight   loop-range ribbon, carved from the TOP of the ruler
HeaderWidth       track-name column (left)
TrackHeight       one lane

_rulerTop   = _origin.Y + TransportHeight
_loopBottom = _rulerTop + LoopStripHeight   (loop strip = [_rulerTop, _loopBottom); scrub/labels below)
_lanesTop   = _rulerTop + RulerHeight
_laneLeft   = _origin.X + HeaderWidth
```

Time transform: `FrameToX(f) = _laneLeft + f*_pxPerFrame - _scrollX`; inverse `XToFrame`.
Vertical: `TrackTop(i) = _lanesTop + i*TrackHeight - _scrollY`. Each region draws inside its
own `dl.PushClipRect(...)` so nothing bleeds across the dividers. Interaction is fully manual
hit-testing over one big `InvisibleButton`; the only real ImGui widget used is the `InputText`
overlay for inline rename (same trick as Automata — `HandleInteraction` is skipped while an
inline edit is open).

## Known issues / rough edges

- **DPI: the editor renders 2× on 200% Windows scaling** (shared with Automata — see the
  `vl-automata-editor-dpi-pixel-lock` memory). This is why several sizing constants (ruler
  height especially) were bumped by hand. A proper fix is to fold `1/DIPFactor` into the editor
  scale rather than chasing pixel constants — **not done yet**.
- Playback is real-time by default (dt × Fps); render-rate-locked stepping is now opt-in via the
  hidden `Frame Step Playback` pin (see timebase decision above). Real-time still relies on ImGui's
  `io.DeltaTime` being sane — a very long stall produces one large catch-up jump (bounded on loop).
- Pan/zoom is **not** carried across an external `Data` reload (Automata carries pan/zoom over
  a rebuild; Interpole rebuilds the widget and resets the view). Low priority.
- **The playhead/timecode readout is drawn at the base font size** (`ReadoutTextScale = 1`). It was
  `2.4×`, but the node loads no font (`fonts: Spread<FontConfig?>.Empty`), so ImGui uses its default
  **bitmap atlas** font (ProggyClean, baked ~13px) — magnifying that looked pixelated. A crisp
  *large* readout needs a dedicated bigger font baked into the atlas via a `FontConfig` on the node's
  `ToSkiaLayer.Update` (and drawing the readout with `dl.AddText(thatFont, thatFont.FontSize, …)`
  instead of scaling) — **not done**; kept small for now.

## Next steps

1. **On-screen verification in gamma** of all interactions (this is where iteration is now).
2. **Optional `Time`/playhead input pin** to drive the playhead from the patch (deterministic,
   better for export) instead of the internal clock. (Done: the **playback outputs** —
   `Current Frame` + per-track `Active Clip Name` / `Clip Frame` / `Clip Length` / `Fade Gain` /
   `Active Color`, aligned `Spread`s evaluated at the playhead; and playback timing itself — internal real-time via `dt × Fps`, legacy
   mode behind `Frame Step Playback`.)
3. **DPI pixel-lock** (fold `1/DIPFactor` into the editor scale).
4. Nice-to-haves: carry pan/zoom across an external reload; ripple/push on cross-track drop (today
   it's strict exact-fit); a crisp large readout font (see Known issues). (Done: shift-snapping;
   marquee multi-select; multi-clip copy/paste; group drag; cross-track drag; loop region + ribbon +
   `Loop In`/`Loop Out` pins.)

## Build

```
dotnet build "VL.Interpole/VL.Interpole.csproj" -c Debug
```

Clean build = 0 warnings, 0 errors. `[ProcessNode]` hot-reloads in gamma on rebuild.
