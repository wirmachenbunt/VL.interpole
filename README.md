# VL.Interpole — a clip timeline editor for vvvv gamma

A Premiere-style **clip timeline** node for vvvv gamma: tracks on the left, a frame-based
clip area on the right, a scrubbable **playhead**, and a **transport bar** (play / stop /
rewind / loop). The timeline is authored in an ImGui editor rendered as a Skia `ILayer` and
is **stored inside the `.vl` patch**.

It is built on the proven plumbing of its sibling project **VL.Automata** (same author): the
ImGui-as-`ILayer` rendering, the DIPTopLeft space fix, and the "persist data in the patch"
technique are all carried over. See [DEVNOTES.md](DEVNOTES.md) for the *why* and the
vvvv-specific gotchas.

## Layout

```
Interpole/
  VL.Interpole/
    VL.Interpole.csproj          net8.0-windows, refs VL.Core + VL.ImGui + VL.ImGui.Skia
    Properties/AssemblyInfo.cs   [assembly: ImportAsIs]  (so vvvv imports the node)
    NuGet.config                 nuget.org + vvvv-public-feed
    Engine/InterpoleData.cs      the document model (tracks / clips + colour + fades / playhead), JSON I/O
    Editor/InterpoleEditorWidget.cs  the timeline editor (ImGui DrawList)
    Nodes/InterpoleNode.cs       the [ProcessNode] exposed in gamma
  interpole_testbed.vl           scratch patch for trying the node
  README.md / DEVNOTES.md
```

## Node

`[ProcessNode(Name = "Interpole", Category = "Interpole")]`.

| direction | pin | type | meaning |
|-----------|-----|------|---------|
| in | `Data` | string | the timeline as JSON, **stored in the patch** — leave unconnected; the node reads and writes it (see [Persistence](#persistence-data-pin)). You can still paste JSON in to load a timeline, or copy it out. |
| in | `No UI Mode` | bool | when true, skip the ImGui frame + `ToSkiaLayer` update and output a null `Editor` — flip on in a running/exported patch to reclaim the per-frame editor cost. Default false = UI on. |
| in | `No Background` | bool | **inspector-only** (`PinVisibility.Optional`). When true the editor canvas is transparent (fill + ImGui window background dropped) so the renderer shows through. Default false = opaque. |
| in | `Fps` | int | **inspector-only** (`PinVisibility.Optional`). The timeline's frame rate: it drives **real-time playback** (below) *and* the **timecode** display. The model stays in integer frames. Default **60**; clamped `>= 1`. |
| in | `Frame Step Playback` | bool | **inspector-only** (`PinVisibility.Optional`). Playback timing mode. Default **false** = real-time: the playhead advances by elapsed time × `Fps`, so one second of playback is one wall-clock second regardless of render rate. **true** = legacy: advance one frame per rendered frame (bound to the renderer's FPS). Independent of the stopwatch ruler toggle. |
| out | `Editor` | ILayer (Skia) | the editor as a Skia layer — wire straight into a renderer. |

A freshly dropped node seeds itself with **Track 1** (two sample clips) + **Track 2** so you
see a working timeline immediately.

> **The node is still editor-only — no domain output pins yet.** Playback runs internally and the
> playhead lives in the document (`Playhead`, int frame), but nothing is *emitted*: there is no
> **active-clip-per-track**, **current-frame**, or **fade-gain** output, and no optional `Time`
> **input** to drive the playhead from the patch. Colours and fades are authored but not yet
> consumed downstream. That's the planned next step (see
> [DEVNOTES → Next steps](DEVNOTES.md#next-steps)).

## Editor

`Editor` is already a Skia **`ILayer`** — the node wraps the ImGui widget with
`VL.ImGui.ToSkiaLayer` internally, so wire it straight into a renderer:

```
Interpole.Editor  →  Skia renderer / RenderWindow
```

**No renderer setting needed** — the node forces the layer into **`DIPTopLeft`** space
(VL.Skia's `SetSpaceUpstream2`), so rendering **and mouse input** work 1:1 from the top-left
even in the renderer's **default** `Normalized` Space.

### Bands (top → bottom)

1. **Transport bar** — timecode/frame readout on the left, **Play ▶ / Stop ■ / Rewind ⏮ /
   Loop ⟳** buttons centered, and a **stopwatch** toggle button on the right that switches the
   ruler + readout between **timecode** (`M:SS` / `M:SS:FF`, at the `Fps` pin's rate) and raw
   **frame numbers**. Defaults to **timecode** (stopwatch on).
2. **Ruler** (frame numbers or timecode, per the stopwatch toggle) + top-left **corner** holding
   the **+ Track** button.
3. **Clip lanes** (right) aligned with the **track-header column** (left).

### Inspector (right panel)

A **collapsible inspector** on the right edits the **selected clip** — it **starts collapsed**;
click the ▸/◂ **strip** at the far right to open/close it. When several clips are selected it shows a
count and edits the **primary** (last-clicked) clip. It shows editable fields for the clip:

- **Name** (text)
- **Color** (the 10-swatch palette)
- **Start**, **Length**, **Fade In**, **Fade Out** — shown in **frames** or **seconds** depending
  on the stopwatch (timecode) toggle; drag or Ctrl-click to type. Numeric edits clamp to the
  non-overlap rules, and each field edit is one undo step.

### Interactions

Tracks (header column):
- **+ Track** button, or **double-click** empty header area → add a track
- **double-click** a track header → rename it (inline)
- **Alt-click** or **Middle-click** a track header → delete the track (and its clips)

Clips (lanes) — clips in a track can never overlap; all edits clamp to neighbours:
- **click** a clip → select it (editable in the right-side **inspector**); **Shift/Ctrl-click** a
  clip → add/remove it from the selection; **drag an empty lane** → **marquee-select** every clip
  the box touches (hold Shift/Ctrl to add to the current selection); click an empty lane → deselect
- **Ctrl+C / Ctrl+V** copy/paste the **whole selection** at the **playhead** — the earliest clip
  lands on the playhead and the rest keep their relative track + time; each pasted clip is trimmed
  to the gap on its track so it never overlaps
- **Del / Backspace** → delete every selected clip
- **double-click** empty lane → add a clip there
- **drag** a clip body → move it in time; **drag a selected clip** → move the **whole selection**
  together; **drag up/down onto another track** → move the clip(s) to that track (only when the
  whole selection fits there without overlapping — otherwise it stays put and just slides in time)
- **drag** a clip's **left/right edge** → resize it (min length 1 frame; resize stays single-clip)
- **hold Shift** while moving/resizing → snap the clip edge to other clips' edges/fades, frame 0,
  or the **playhead** (a cyan guide marks the snap)
- **drag** a clip's **top-left / top-right handle** inward → set the **fade-in / fade-out**
  (frames); the fade shows as a ramp triangle. Snug in the corner = no fade. The handles sit in
  the top corners; the rest of each edge stays resize
- **double-click** a clip → rename it (inline); a **10-colour palette** appears below the field —
  click a swatch to colour the clip (the choice is saved when the rename commits)
- **Alt-click** or **Middle-click** a clip → delete it

Playhead & view:
- **click / drag in the ruler** → move the playhead (snaps to whole frames). **Hold Shift** for
  After-Effects-style snapping — the playhead snaps to the nearest clip **start/end** or **fade
  handle** (and frame 0), and turns cyan while snapped
- **Space** → toggle play / stop
- **Play** advances the playhead in real time (`Fps` frames per second by default; see the
  `Frame Step Playback` pin for the legacy per-render-frame mode) — pressing Play while parked at
  the end restarts from 0; **Loop** wraps it at the last clip's end; **Rewind** jumps to 0;
  **Stop** halts and saves the position. While playing, if the playhead scrolls out of view the
  timeline **auto-pages** to keep it visible
- **mouse wheel** over the lanes → zoom the time axis (around the cursor)
- **right-drag** → pan the view (time + tracks)
- **stopwatch** button (transport bar, right) → toggle the ruler + playhead readout between
  timecode and frame numbers (display only — the model stays in frames)

History:
- **Ctrl+Z** undo, **Ctrl+Y** / **Ctrl+Shift+Z** redo — covers structural edits (add/remove/
  move/resize/rename of tracks & clips). Playhead position is left untouched by undo/redo. This is
  the editor's own history, separate from vvvv's undo (the `Data`-pin autosave stays out of it).

All units are **integer frames**; the **stopwatch** toggle only changes how the ruler and
readout are *labelled* (timecode is derived from the inspector-only **`Fps`** pin, default 60).

## Persistence (`Data` pin)

The timeline lives on the **`Data`** input pin as JSON and is **saved inside the `.vl`
patch** — no external file. **Leave `Data` unconnected**: the node reads it to load the
timeline and writes the serialized document back into it on edits, via the vvvv solution API
(`ISolution.SetPinValue`, without recompiling or adding an undo step). This is the gamma
equivalent of beta's "save to a hidden pin" (VL.Audio.VST persists its state chunk the same
way). Because it's a real pin you can copy the JSON out or paste one in.

The **playhead** is part of the document, but playback-driven changes are **not** saved every
frame (that would hammer the autosave) — only manual scrubs, **Stop** and **Rewind** persist
it. See [DEVNOTES](DEVNOTES.md).

## Dev loop (local gamma)

1. `dotnet build -c Debug` (or `Release`) →
   `VL.Interpole/bin/Debug/net8.0-windows/VL.Interpole.dll`.
2. In gamma, reference the built `VL.Interpole.dll` (or the `.csproj`).
3. Drop the **Interpole** node (category `Interpole`). It seeds a sample timeline and stores
   it on `Data` — no file to set up.
4. Wire `Editor` straight into a renderer / RenderWindow to open the editor (no renderer Space
   change needed). Author the timeline; it's saved with the patch.
5. `[ProcessNode]` classes hot-reload on rebuild.

## Status

**Built, clean (0 warnings). On-screen verification in gamma is ongoing** — that's where iteration
happens. `[ProcessNode]` hot-reloads on rebuild.

Done:
- **Tracks** — add / rename / delete.
- **Clips** — add / move / resize / rename / delete, non-overlapping (neighbour-clamped); **drag
  between tracks** (single clip or whole selection, when it fits); a **10-colour palette** (picked
  from the rename field); **per-clip fade-in/out** via top-corner handles (ramp triangles).
- **Selection** — click, **Shift/Ctrl-click**, or **marquee** (drag an empty lane); **group drag**
  moves all selected clips together (incl. across tracks); **Del/Backspace** deletes the selection.
- **Inspector** — a collapsible right-side panel edits the (primary) selected clip's name, colour,
  and start/length/fades (frames or seconds per the ruler mode).
- **Clipboard** — Ctrl+C / Ctrl+V copy/paste the **whole selection** at the playhead (relative
  track + time preserved).
- **View** — right-drag pan, wheel zoom (around the cursor), **playhead auto-follow** while playing
  (and on Rewind).
- **Transport** — Play / Stop / Rewind / Loop; **Space** = play/stop; **real-time playback**
  (`Fps`-based, delta-time), with a hidden **Frame Step Playback** legacy mode;
  Play-while-parked-at-end restarts from 0.
- **Ruler / readout** — **timecode ⇄ frames** stopwatch toggle (timecode derived from `Fps`),
  timecode on by default.
- **Undo / redo** — editor-local, structural edits only, **Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z**.
- **Persistence** — the whole timeline lives on the `Data` pin, saved inside the `.vl` patch.
- **Pins** — `Data`, `No UI Mode`, `Editor` out; inspector-only `No Background`, `Fps`,
  `Frame Step Playback`.

Not done yet (see [DEVNOTES → Next steps](DEVNOTES.md#next-steps)):
- **Domain output pins** — active-clip-per-track (aligned `Spread`s), current frame, and per-track
  **fade gain (0..1)** evaluated at the playhead. This is the big one: it turns the node from an
  editor into a usable timeline, and it's what makes the authored colours/fades actually *do*
  something downstream.
- **Optional `Time`/playhead input pin** — drive the playhead from the patch (deterministic, better
  for export) instead of the internal clock.
- **DPI pixel-lock** — the editor renders 2× on 200% Windows scaling (shared with Automata).
- **Nice-to-haves** — loop in/out markers; carry pan/zoom across an external `Data` reload;
  ripple/push on cross-track drop (today it's strict exact-fit); a crisp large timecode-readout font
  (currently the readout is drawn at the base font size — see [DEVNOTES](DEVNOTES.md#known-issues--rough-edges)).
