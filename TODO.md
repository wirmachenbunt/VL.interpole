# VL.Interpole — TODO

A running list of **potential** future work. Nothing here is committed or scheduled — it's a parking
lot for ideas. The shipped-feature history and the "resume-here" engineering notes live in
[DEVNOTES.md](DEVNOTES.md) (see its *Next steps* section for the nearer-term backlog); this file is for
looser, not-yet-decided ideas.

## Potential

### Keyframe channels — "vvvv channel" keyframe tracks
Allow adding a keyframe track that is a **vvvv Channel** (VL.Core `IChannel` / `Channel<T>`), alongside
the existing per-clip float tracks. The animated keyframe value would drive (or bind to) a named gamma
channel, so the timeline plugs into vvvv's standard channel-based data flow instead of only emitting a
`Spread<float>` through `GetKeyframes`.

Open questions / things to decide when picking this up:
- **Direction:** does the track *write* its sampled value into a channel (timeline → channel), or can a
  channel also *drive* a track (channel → timeline), or both?
- **Identity:** how is the channel named/resolved — a string key on the `FloatTrackData`, a channel pin,
  or the global channel registry?
- **Type:** float first (matches today's tracks); later vec2/3/4 + colour channel tracks (ties into the
  "typed keyframe lanes" idea already noted as *not built* in DEVNOTES' keyframe section).
- **Editor UI:** how a channel track is created and shown on the clip keyframe page vs a plain float
  track (a badge? a different header control? the `+ Float Track` button gaining a type choice?).
- **Output surface:** a new `Get…`/splitter node, or fold into the existing `GetKeyframes` output.
