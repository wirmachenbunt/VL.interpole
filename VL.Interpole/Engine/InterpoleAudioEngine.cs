using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VL.Audio;

namespace VL.Interpole.Engine;

// The audio side of Interpole: a sample-accurate mixer that plays the timeline's audio clips through
// VL.Audio's engine. It is a MultiChannelSignal (stereo) — the VL.Audio engine pulls it on the audio
// thread via FillBuffers, and its two Outputs are what the node exposes as a Spread<AudioSignal>.
//
// Model:
//   • The node builds an immutable AudioTransport (the clip layout in *seconds* + play/loop state)
//     each frame on the main thread and hands it to the mixer via SetTransport — the audio thread
//     only ever reads that reference, never the live document.
//   • The mixer owns a sample position (engine-rate) that it advances while playing; the node reads
//     PositionSeconds back to drive the visual playhead (audio-master clock).
//   • Each clip gets an AudioClipSource (an NAudio file reader + resampler) opened off-thread and
//     read sequentially in lock-step with the timeline; sources are pooled by clip id and disposed
//     on the audio thread when their clip leaves the snapshot.
//
// Everything the audio thread touches (the source pool, the position) is touched *only* on the audio
// thread; cross-thread hand-off is the volatile transport reference + a locked pending-seek + the
// interlocked position. Timeline geometry is stored in seconds so it is independent of the engine
// sample rate (which the mixer reads live and can change under us).

// One audio clip as the audio thread needs it: geometry in seconds, plus a stable key (the clip id)
// so its file source survives snapshot rebuilds. Immutable.
public sealed class AudioClipSnap
{
    public readonly string Key;         // clip id — stable across edits, keys the source pool
    public readonly string FilePath;
    public readonly double StartSec;    // timeline position of the clip's start
    public readonly double LengthSec;   // how long the clip sounds on the timeline
    public readonly double InPointSec;  // where in the file playback begins (trim head)
    public readonly double FadeInSec;
    public readonly double FadeOutSec;
    public readonly float Gain;

    public AudioClipSnap(string key, string filePath, double startSec, double lengthSec,
                         double inPointSec, double fadeInSec, double fadeOutSec, float gain)
    {
        Key = key; FilePath = filePath;
        StartSec = startSec; LengthSec = lengthSec; InPointSec = inPointSec;
        FadeInSec = fadeInSec; FadeOutSec = fadeOutSec; Gain = gain;
    }

    public double EndSec => StartSec + LengthSec;
}

// Immutable transport snapshot swapped into the mixer atomically (by reference) each frame.
public sealed class AudioTransport
{
    public readonly bool Playing;
    public readonly bool Looping;
    public readonly double LoopInSec;
    public readonly double LoopOutSec;
    public readonly IReadOnlyList<AudioClipSnap> Clips;

    public AudioTransport(bool playing, bool looping, double loopInSec, double loopOutSec, IReadOnlyList<AudioClipSnap> clips)
    {
        Playing = playing; Looping = looping;
        LoopInSec = loopInSec; LoopOutSec = loopOutSec;
        Clips = clips;
    }

    public static readonly AudioTransport Idle = new(false, false, 0, 0, Array.Empty<AudioClipSnap>());

    // Build a transport snapshot from the live document (main thread). All timeline geometry is
    // converted from integer frames to seconds here (via fps), so the mixer stays rate-independent.
    // Only audio clips on any track are included (clip.IsAudio); non-audio clips are ignored.
    public static AudioTransport Build(InterpoleDocument doc, int fps, bool playing, bool looping)
    {
        double f = Math.Max(1, fps);
        var clips = new List<AudioClipSnap>();
        foreach (var tr in doc.Tracks)
            foreach (var c in tr.Clips)
            {
                if (!c.IsAudio) continue;
                var (fi, fo) = c.EffectiveFades();
                clips.Add(new AudioClipSnap(
                    c.Id, c.FilePath!,
                    c.Start / f, c.Length / f, c.FileInPoint,
                    fi / f, fo / f, c.Gain));
            }

        var (li, lo) = doc.EffectiveLoop();
        return new AudioTransport(playing, looping, li / f, lo / f, clips);
    }
}

public sealed class InterpoleMixerSignal : MultiChannelSignal
{
    // The current clip layout + transport (main thread writes, audio thread reads). Reference
    // assignment is atomic; volatile makes the audio thread see the latest.
    private volatile AudioTransport _transport = AudioTransport.Idle;

    // Timeline position in engine-rate samples — owned by the audio thread, read by the node.
    private long _positionSamples;

    // A one-shot seek request (seconds), applied at the top of the next FillBuffers.
    private readonly object _seekLock = new();
    private double? _pendingSeekSec;

    // Environment.TickCount64 of the last FillBuffers — the node uses recency to tell whether the
    // VL.Audio engine is actually pulling us (so it knows to hand the clock over to audio).
    private long _lastFillTick;

    // The source pool — audio-thread-only. Keyed by clip id.
    private readonly Dictionary<string, AudioClipSource> _sources = new();
    private float[] _tmp = Array.Empty<float>();
    private readonly HashSet<string> _liveKeys = new();

    // MultiChannelSignal() already SetOutputCount(2) → stereo. Outputs is inherited.

    public void SetTransport(AudioTransport t) => _transport = t;

    public double PositionSeconds
    {
        get { int r = SampleRate; return r > 0 ? Interlocked.Read(ref _positionSamples) / (double)r : 0; }
    }

    // Jump the mixer's play position (seconds). Applied on the next audio buffer; forces every active
    // source to reseek so playback resumes cleanly from the new spot.
    public void RequestSeek(double sec) { lock (_seekLock) _pendingSeekSec = Math.Max(0, sec); }

    // Milliseconds since the audio engine last pulled us; large/⧜ when nothing is wired to Outputs.
    public long MillisSinceLastFill => Environment.TickCount64 - Interlocked.Read(ref _lastFillTick);

    protected override void FillBuffers(float[][] buffers, int offset, int count)
    {
        Interlocked.Exchange(ref _lastFillTick, Environment.TickCount64);

        // Start silent; clips add into [0, count). (FReadBuffers is the parent's 0-based scratch —
        // the per-channel SingleSignal copies it out with the right offset, so we ignore `offset`
        // here, exactly as FileStreamSignal does.)
        int nch = buffers.Length;
        for (int c = 0; c < nch; c++) Array.Clear(buffers[c], 0, count);

        int rate = SampleRate;
        if (rate <= 0) return;

        var t = _transport;

        // Apply a pending seek before anything else.
        double? seek = null;
        lock (_seekLock) { if (_pendingSeekSec is double s) { seek = s; _pendingSeekSec = null; } }
        if (seek is double sk) { Interlocked.Exchange(ref _positionSamples, (long)(sk * rate)); ForceReseekAll(); }

        PruneSources(t);
        if (!t.Playing || t.Clips.Count == 0) return;

        long pos = Interlocked.Read(ref _positionSamples);
        int done = 0;
        int guard = 0;
        while (done < count)
        {
            if (++guard > count + 4) break;   // paranoia against a zero-length loop region
            int seg = count - done;

            // Split the buffer at the loop-out boundary so each segment is contiguous in time.
            if (t.Looping && t.LoopOutSec > t.LoopInSec)
            {
                long loIn = (long)(t.LoopInSec * rate);
                long loOut = (long)(t.LoopOutSec * rate);
                if (pos >= loOut) { pos = loIn; ForceReseekAll(); continue; }
                seg = (int)Math.Min(seg, loOut - pos);
                if (seg <= 0) { pos = loIn; ForceReseekAll(); continue; }
            }

            MixSegment(buffers, done, seg, pos, rate, t);
            pos += seg;
            done += seg;
        }
        Interlocked.Exchange(ref _positionSamples, pos);
    }

    // Mix all clips that overlap [segStartPos, segStartPos+seg) (engine samples) into the output at
    // [outOffset, outOffset+seg). Each source is read sequentially; it is reseeked only when the run
    // is discontinuous (a fresh clip, a seek, or a loop wrap — signalled by NextTimelinePos != a).
    private void MixSegment(float[][] buffers, int outOffset, int seg, long segStartPos, int rate, AudioTransport t)
    {
        int nch = buffers.Length;
        foreach (var clip in t.Clips)
        {
            long clipStart = (long)(clip.StartSec * rate);
            long clipEnd = (long)(clip.EndSec * rate);
            long a = Math.Max(segStartPos, clipStart);
            long b = Math.Min(segStartPos + seg, clipEnd);
            if (b <= a) continue;   // clip not active in this segment

            var src = EnsureSource(clip, rate);
            if (src is null || !src.Ready) continue;

            int frames = (int)(b - a);
            if (src.NextTimelinePos != a)   // discontinuous → reposition the file
            {
                double fileSec = clip.InPointSec + (a - clipStart) / (double)rate;
                src.SeekSeconds(fileSec);
                src.NextTimelinePos = a;
            }

            int ch = src.Channels;
            EnsureTmp(frames * ch);
            int got = src.ReadFrames(_tmp, frames);
            src.NextTimelinePos += got;   // advance by what we actually read (0 past file end)

            int localOff = (int)(a - segStartPos) + outOffset;
            for (int i = 0; i < got; i++)
            {
                double ts = (a + i) / (double)rate;
                float g = GainAt(clip, ts);
                float l = _tmp[i * ch] * g;
                float r = (ch > 1 ? _tmp[i * ch + 1] : _tmp[i * ch]) * g;
                buffers[0][localOff + i] += l;
                if (nch > 1) buffers[1][localOff + i] += r;
            }
        }
    }

    // Linear fade envelope (× clip gain) at a timeline time in seconds — the smooth, per-sample
    // version of ClipData.GainAt, so audio fades don't zipper at frame boundaries.
    private static float GainAt(AudioClipSnap clip, double timelineSec)
    {
        double into = timelineSec - clip.StartSec;
        double left = clip.EndSec - timelineSec;
        float g = clip.Gain;
        if (clip.FadeInSec > 0 && into < clip.FadeInSec) g *= (float)(into / clip.FadeInSec);
        if (clip.FadeOutSec > 0 && left < clip.FadeOutSec) g *= (float)(left / clip.FadeOutSec);
        return g < 0f ? 0f : g;
    }

    private AudioClipSource? EnsureSource(AudioClipSnap clip, int rate)
    {
        if (_sources.TryGetValue(clip.Key, out var src))
        {
            if (src.FilePath == clip.FilePath) return src;
            src.Dispose();                 // same clip now points at a different file
            _sources.Remove(clip.Key);
        }
        var created = new AudioClipSource(clip.FilePath, rate);
        _sources[clip.Key] = created;
        return created;
    }

    private void ForceReseekAll()
    {
        foreach (var s in _sources.Values) s.NextTimelinePos = long.MinValue;
    }

    // Dispose + drop any pooled source whose clip is no longer in the snapshot.
    private void PruneSources(AudioTransport t)
    {
        if (_sources.Count == 0) return;
        _liveKeys.Clear();
        foreach (var c in t.Clips) _liveKeys.Add(c.Key);
        List<string>? dead = null;
        foreach (var kv in _sources)
            if (!_liveKeys.Contains(kv.Key)) (dead ??= new()).Add(kv.Key);
        if (dead is null) return;
        foreach (var k in dead) { _sources[k].Dispose(); _sources.Remove(k); }
    }

    private void EnsureTmp(int n) { if (_tmp.Length < n) _tmp = new float[n]; }

    public override void Dispose()
    {
        foreach (var s in _sources.Values) s.Dispose();
        _sources.Clear();
        base.Dispose();
    }
}

// One clip's audio source: an NAudio file reader resampled to the engine rate, opened off-thread so
// the audio thread never blocks on IO. Until the open completes (or if it fails) it reads as silence.
// All playback calls (SeekSeconds/ReadFrames) happen on the audio thread; Open runs once on a Task.
internal sealed class AudioClipSource : IDisposable
{
    public readonly string FilePath;
    private readonly int _engineRate;

    private volatile bool _ready;
    private volatile bool _failed;
    private volatile bool _disposeRequested;
    private AudioFileReader? _reader;
    private ISampleProvider? _chain;
    private int _channels = 2;

    // Mixer bookkeeping (audio thread only): the timeline sample index this source is positioned to
    // read next. long.MinValue means "unknown — reseek before reading".
    public long NextTimelinePos = long.MinValue;

    public AudioClipSource(string filePath, int engineRate)
    {
        FilePath = filePath;
        _engineRate = engineRate;
        Task.Run(Open);
    }

    private void Open()
    {
        try
        {
            var reader = new AudioFileReader(FilePath);
            _reader = reader;
            _channels = Math.Max(1, reader.WaveFormat.Channels);
            BuildChain();
            _ready = true;
            if (_disposeRequested) DisposeInner();
        }
        catch { _failed = true; }
    }

    // (Re)build the resample chain from the reader's current position, flushing any resampler state.
    private void BuildChain()
    {
        var reader = _reader!;
        _chain = reader.WaveFormat.SampleRate == _engineRate
            ? reader
            : new WdlResamplingSampleProvider(reader, _engineRate);
    }

    public bool Ready => _ready && !_failed;
    public int Channels => _channels;

    public void SeekSeconds(double sec)
    {
        if (!_ready || _reader is null) return;
        var t = TimeSpan.FromSeconds(Math.Max(0, sec));
        if (t > _reader.TotalTime) t = _reader.TotalTime;
        _reader.CurrentTime = t;
        BuildChain();   // flush the resampler so playback continues cleanly from the new spot
    }

    // Read up to frameCount interleaved frames (engine rate, Channels wide) into dst; returns the
    // number of frames actually read (fewer at end of file).
    public int ReadFrames(float[] dst, int frameCount)
    {
        if (!Ready || _chain is null) return 0;
        int got = _chain.Read(dst, 0, frameCount * _channels);
        return got / _channels;
    }

    public void Dispose()
    {
        _disposeRequested = true;
        if (_ready) DisposeInner();   // if still opening, Open() disposes on completion
    }

    private void DisposeInner()
    {
        try { _reader?.Dispose(); } catch { }
        _reader = null;
        _chain = null;
    }
}
