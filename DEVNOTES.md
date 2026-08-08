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
  neighbour's start; every move/resize/add clamps against them. Neighbour identity stays stable
  during a drag because we clamp *before* the clip could cross a neighbour. `AddClip` finds the
  gap under the cursor and shrinks the new clip to fit (skips if < 1 frame).
- **Clips stay in their own track.** v1 has no vertical drag between tracks (keeps the
  non-overlap logic per-track and simple). See Next steps.
- **Per-clip fades are lengths in frames, edited by top-corner handles.** `ClipData.FadeIn` /
  `FadeOut` are frame lengths from each end (`[Start, Start+FadeIn)`, `[End-FadeOut, End)`), kept
  `>= 0` with `FadeIn + FadeOut <= Length` (`ClampFades`, also re-run after a resize shrinks the
  clip). `Fades()` returns clamped effective values so drawing/hit-testing is robust to bad
  persisted data. Each clip draws a small handle square on both top corners (`FadeHandleRect` —
  centred on the fade end, clamped inside the clip); the fade renders as a dimmed corner triangle
  plus a gain line from the bottom corner up to the handle. In `HitClip` the fade handles take
  priority over the resize edge they overlap, but only in the top ~11px, so the rest of an edge is
  still resize (new `ClipZone`/`DragMode` FadeIn/FadeOut). Fades are **document + editor only so
  far** — no output pin applies them yet (see Next steps, alongside the other playback outputs).
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
- **Clip selection + clipboard (copy/paste).** A single `_selectedClipId` (set on clip click,
  cleared on empty-lane click / delete) draws a yellow highlight and is the copy source; `AddClip`
  and `PasteClip` select their new clip. `Ctrl+C` clones the selected clip (or, as a fallback, the
  hovered one) into `_clipboard` + remembers its track (`_clipboardTrackId`). `Ctrl+V` pastes into
  that track (or the first, if it's gone) **at the playhead**: the start is pushed past any clip
  covering the playhead, then the length is trimmed to the gap before the next clip (so non-overlap
  holds); no room → no-op. Fades are re-clamped after a trim. The paste is one undo step (`Commit`).
  Selection is by id, so it survives undo/redo (ids are stable in the JSON). Shortcuts live in
  `HandleShortcuts`, suppressed while an inline rename owns the keyboard.
- **Clip colour is a palette index, not an RGB value.** `ClipData.Color` is an `int` index into a
  fixed 10-entry `ClipPalette` in the editor (index 0 = the original blue, so old clips are
  unchanged; out-of-range falls back to 0 via `ClipRgb`). Storing the index keeps the JSON compact
  and lets the palette be re-tuned in one place. The swatch strip is drawn/hit-tested manually in
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
- **Shift snaps the playhead scrub *and* clip move/resize.** Shared helpers: `CollectClipEdgeTargets`
  fills `_snapTargets` with frame 0 + every clip's `Start`/`End`/fade-in end/fade-out start (an
  `exclude` clip is skipped so a dragged clip can't snap to itself); `Snap(frame)` returns the
  nearest target within `SnapPx` (8 px, pixel-based → zoom-independent) plus the pixel distance.
  - **Playhead** (`PlayheadFrameFromMouse`, both ruler click and drag): targets are all clips (no
    exclude, playhead not a target); `_playheadSnapped` tints the playhead cyan + thicker.
  - **Clip drag** (`ContinueClipDrag`): targets exclude the dragged clip and *add the playhead*.
    Move snaps whichever edge (start/end) is closer; resize snaps the dragged edge. The snap is
    dropped if a neighbour bound overrides it (`ns == cand` / `== _clipSnapFrame` check after
    clamp). `_clipSnapActive`/`_clipSnapFrame` drive a cyan vertical guide (`DrawSnapGuide`). Note
    the snap state is set in `HandleInteraction` (after the draw calls), so the guide follows by one
    frame — same 1-frame lag as the dragged clip itself, so it stays consistent.
- **Loop** wraps at `ContentEnd()` (the max clip end across all tracks); there are no explicit
  loop in/out markers yet. `_playing` / `_loop` are session-transient widget fields, not saved.
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
  **Space** play/stop, **Ctrl+Z** undo, **Ctrl+Y / Ctrl+Shift+Z** redo, **Ctrl+C/V** copy/paste clip.

## Editor geometry (how the bands are laid out)

Everything is computed each frame from `_origin` + these constants:

```
TransportHeight   transport toolbar (top, full width)
RulerHeight       frame ruler
HeaderWidth       track-name column (left)
TrackHeight       one lane

_rulerTop = _origin.Y + TransportHeight
_lanesTop = _rulerTop + RulerHeight
_laneLeft = _origin.X + HeaderWidth
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

## Next steps

1. **On-screen verification in gamma** of all interactions (this is where iteration is now).
2. **Playback outputs**: outputs like **active-clip-per-track** (aligned `Spread`s) + the current
   playhead frame — plus a **per-track fade gain** (0..1) evaluated from the active clip's
   `FadeIn`/`FadeOut` at the playhead — turning it from an editor into a usable timeline. Playback timing itself is
   done (internal real-time via `dt × Fps`, legacy mode behind `Frame Step Playback`); what remains
   is an optional `Time`/playhead **input** pin to drive the playhead from the patch (deterministic,
   better for export) instead of the internal clock.
3. **DPI pixel-lock** (fold `1/DIPFactor` into the editor scale).
4. Nice-to-haves: drag clips between tracks; explicit loop in/out markers; carry pan/zoom across an
   external reload. (Shift-snapping — playhead + clip move/resize — is done.)

## Build

```
dotnet build "VL.Interpole/VL.Interpole.csproj" -c Debug
```

Clean build = 0 warnings, 0 errors. `[ProcessNode]` hot-reloads in gamma on rebuild.
