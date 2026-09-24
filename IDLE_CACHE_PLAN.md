# Idle bitmap-cache plan (extending the playmode snapshot cache to edit mode)

> Status: **design only, not implemented.** Captured 2026-09-24 for later. This is the plan
> discussed for reusing the playmode `SnapshotLayer` optimization while the editor is *idle*
> in normal edit mode, so an untouched editor stops re-rasterizing every frame.

## Goal

The playmode cache already blits a baked still while playing (only playhead + timecode redraw
live). We want the same win when the editor is **stopped but idle** — no hover over interactive
elements, no active edit. The editor should stay fully editable; the moment you interact it goes
live again, with no visible difference to the user.

Motivation: ImGui is immediate-mode, so an idle editor rebuilds the full DrawList and
`ToSkiaLayer` re-rasterizes the whole opaque window every frame — same per-frame cost as an
active one. That's wasted work when nothing is changing on screen.

## Existing machinery to reuse (already in the repo)

- [`SnapshotLayer.cs`](VL.Interpole/Editor/SnapshotLayer.cs) — modes `Live` / `Capture` /
  `BlitOverlay`, LRU keyed by `PageKey(ScrollPx, PxBucket, W, H, View)`, the `ContentMatrix`
  device-size rebuild, and the destination color-space match.
- [`InterpoleNode.cs:355-390`](VL.Interpole/Nodes/InterpoleNode.cs) — the per-frame driver that
  sets `_snapshot.Mode` + `CurrentKey` and the editor's `PlayPass` before `_toSkia.Update`.
- [`InterpoleEditorWidget.cs:400-475`](VL.Interpole/Editor/InterpoleEditorWidget.cs) — the
  `PlayCache { Off, Static, Overlay }` pass split. Crucially, **interaction/shortcuts still run
  during the Overlay (blit) pass** (lines 461-468), which is what makes idle-caching safe:
  clicks/keys are never lost, only the static *visual* is skipped.

## Core mechanism

Add a fourth pass state (an **idle pass**) that behaves like the play `Overlay` pass:
run all interaction / shortcuts / hit-test, build a near-empty DrawList, blit the cached image.
Difference from play Overlay: **no moving overlay to draw** — when stopped the playhead is parked
and gets baked into the still.

Guiding principle that keeps "no visible difference" true:

> The idle pass runs **all the same logic** as live; it only skips *building and rasterizing the
> static visual*, substituting the cached blit. The instant anything that affects appearance
> changes, flip to live.

When the mouse is off the window (or over inert background) a live render would show no hover
either, so cached == live pixel-for-pixel. Divergence only occurs exactly when we'd go live anyway.

## Cache invalidation (the part that differs from playmode)

Playmode content is static by construction, so it never invalidates mid-play. Edit mode changes
constantly (move/resize clip, keyframe, rename, color, select, pan, zoom, scrub). **Do not build
fine-grained dirty tracking.** Instead:

- **Any live frame invalidates the idle cache.** On the live→idle settle transition, if it's a
  cache miss, `Capture` once; subsequent idle frames `BlitOverlay`.
- Capture is a single cheap frame, so re-capturing on each settle is fine. No document hashing.
- `PageKey` already encodes `W/H/PxBucket/ScrollPx`, so resize / DPI change / pan just misses the
  key and re-captures for free.

## Switching trigger — decision to make before implementing

Two scopes, very different risk:

- **Conservative (recommended first):** cache only when the editor window isn't hovered
  (`IsWindowHovered` false) AND nothing active (no drag, inline edit, menu/dialog, pan/scrub).
  Bulletproof — nothing changes appearance when the mouse isn't over it — and already captures the
  main win (editor sitting in a running patch while you work elsewhere).
- **Aggressive (what was literally asked):** also cache while the mouse *is* over the window but
  not over an interactive element. Needs an audit of everything that reacts to mouse position:
  clip hover tint + waveform reveal, fade grips, resize-edge cursor (`SetMouseCursor`),
  transport/tab/inspector ImGui hover states, marker hover. Miss one → frozen affordance. Doable
  via existing `_hoverClipId` / `_hoverZone` + a "mouse over any chrome" predicate; this is the
  fiddly part.

## Caveats / problems to keep in mind

1. **One-frame switch latency.** The node sets the mode *before* `_toSkia.Update`, so frame N's
   mode is decided from frame N-1's hover state. Crossing onto a clip = one frame (~16 ms) of
   cached-with-no-hover before live kicks in. Clicks/keys are never lost (interaction runs every
   frame); only the hover tint lags one frame. Inherent to the two-layer split unless restructured.
2. **Keyboard/shortcuts must keep running while cached** (e.g. `K` = keyframe). Since the blit pass
   still runs interaction, a keypress mutates the doc → invalidates → next frame live/re-capture.
3. **Cursor-shape feedback near edges** only updates when `SetMouseCursor` runs. In the aggressive
   version, hovering a resize edge must count as "over an element" so we're live there.
4. **Idle animations: none exist** (verified). Stopwatch `Cos/Sin` is a static icon, `_clock` is
   playback-only, caret blink is only during an active inline edit (live). So nothing freezes.
5. **Capture must force hover off** on the settle frame so the baked still is clean (no leftover
   tint from the frame we were still hovering).

## Recommendation

Ship the **conservative** trigger first (low risk, reuses play machinery almost verbatim, most of
the win), then layer the "over inert background" case on top once proven.

Verify the win is where expected: caching saves *rasterization*; the ImGui build cost is only saved
if the idle pass builds a minimal DrawList like Overlay does (don't keep running the full build).
