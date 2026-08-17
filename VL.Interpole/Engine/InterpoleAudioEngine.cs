using NAudio.Dsp;
using NAudio.Wave;
using VL.Audio;

namespace VL.Interpole.Engine;

// The audio side of Interpole: a stereo mixer that plays the timeline's audio clips through VL.Audio's
// engine. It is a MultiChannelSignal — the engine pulls it on the audio thread via FillBuffers, and
// its two Outputs are what the node exposes as a Spread<AudioSignal>.
//
// Sync model — "visual is master, audio loosely follows" (a soft-sync / varispeed chase):
//   • The VISUAL playhead is driven by the node's wall clock exactly as a no-audio timeline is, so it
//     (and every value output derived from it — fades, colours, clip frame) stays smooth and locked to
//     vvvv's main loop. An earlier audio-master design drove the playhead FROM the audio sample clock,
//     which made those outputs jitter against the rest of the patch — this replaces it.
//   • The mixer is told the playhead's position each frame (TargetSec, sub-frame precise) and CHASES
//     it: it keeps its own play position and, when it drifts, nudges playback speed a few percent
//     (a tiny pitch bend, via a live resampler) to ease back into lock — no audible seeks. Only a LARGE
//     jump (Play from stop, a seek, a loop wrap — where TargetSec leaps) triggers a hard reposition.
//
// Threading: the node builds an immutable AudioTransport on the main thread and swaps it in via a
// volatile reference; the audio thread only reads that. The source pool + play position are touched
// only on the audio thread. Geometry is in seconds (rate-independent).

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
    public readonly double TargetSec;   // the visual playhead (sub-frame precise) the audio chases
    public readonly IReadOnlyList<AudioClipSnap> Clips;

    public AudioTransport(bool playing, double targetSec, IReadOnlyList<AudioClipSnap> clips)
    {
        Playing = playing; TargetSec = targetSec; Clips = clips;
    }

    public static readonly AudioTransport Idle = new(false, 0, Array.Empty<AudioClipSnap>());

    // Build a transport snapshot from the live document (main thread). Timeline geometry is converted
    // from frames to seconds via fps (so the mixer is rate-independent). playheadFrames is the *sub-frame
    // continuous* playhead (Playhead + fractional accumulator) so the chase target is smooth, not stepped.
    // Only audio clips (clip.IsAudio) are included.
    public static AudioTransport Build(InterpoleDocument doc, int fps, bool playing, double playheadFrames)
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
        return new AudioTransport(playing, playheadFrames / f, clips);
    }
}

public sealed class InterpoleMixerSignal : MultiChannelSignal
{
    // ---- chase tuning ----
    private const double MaxCorrection = 0.03;   // max speed nudge (±3% pitch) while catching up
    private const double DeadbandSec = 0.005;    // within 5 ms of the target, play at exactly 1.0
    private const double CorrectionTau = 0.4;    // seconds — larger = gentler correction
    private const double HardSyncSec = 0.15;     // drift beyond this snaps (Play / seek / loop wrap)
    private const double ReseekTolSec = 0.004;   // a source is "still aligned" within this of expected

    // The current clip layout + transport (main thread writes, audio thread reads). Reference
    // assignment is atomic; volatile makes the audio thread see the latest.
    private volatile AudioTransport _transport = AudioTransport.Idle;

    // Audio-thread-only play state.
    private double _audioPosSec;
    private bool _wasPlaying;

    // The source pool — audio-thread-only. Keyed by clip id.
    private readonly Dictionary<string, AudioClipSource> _sources = new();
    private float[] _tmp = Array.Empty<float>();
    private readonly HashSet<string> _liveKeys = new();

    // MultiChannelSignal() already SetOutputCount(2) → stereo. Outputs is inherited.

    public void SetTransport(AudioTransport t) => _transport = t;

    protected override void FillBuffers(float[][] buffers, int offset, int count)
    {
        // Start silent; clips add into [0, count). (FReadBuffers is the parent's 0-based scratch — the
        // per-channel SingleSignal copies it out with the right offset, so we ignore `offset`, exactly
        // as FileStreamSignal does.)
        int nch = buffers.Length;
        for (int c = 0; c < nch; c++) Array.Clear(buffers[c], 0, count);

        int rate = SampleRate;
        if (rate <= 0) return;

        var t = _transport;
        PruneSources(t);

        if (!t.Playing || t.Clips.Count == 0)
        {
            _audioPosSec = t.TargetSec;   // stay parked on the playhead so resuming is aligned
            _wasPlaying = false;
            return;
        }

        // Chase the visual playhead. drift = how far the audio is ahead(+)/behind(-) the target.
        double startSec = _audioPosSec;
        double drift = startSec - t.TargetSec;
        bool hard = !_wasPlaying || Math.Abs(drift) > HardSyncSec;
        if (hard) { startSec = t.TargetSec; _audioPosSec = startSec; drift = 0; ForceReseekAll(); }
        _wasPlaying = true;

        // Speed correction: ahead → play <1.0 (slow down), behind → >1.0 (speed up). Small drift inside
        // the deadband plays at exactly 1.0 so steady playback isn't constantly micro-pitched.
        double corr = 0;
        double ad = Math.Abs(drift);
        if (ad > DeadbandSec) corr = -(ad - DeadbandSec) * Math.Sign(drift) / CorrectionTau;
        double speed = Math.Clamp(1.0 + corr, 1.0 - MaxCorrection, 1.0 + MaxCorrection);

        double secPerOut = speed / rate;                 // timeline seconds advanced per output sample
        double spanSec = count * secPerOut;              // this buffer's timeline span
        double endSec = startSec + spanSec;

        foreach (var clip in t.Clips)
        {
            double a = Math.Max(startSec, clip.StartSec);
            double b = Math.Min(endSec, clip.EndSec);
            if (b <= a) continue;   // clip not active in this buffer

            var src = EnsureSource(clip);
            if (src is null || !src.Ready) continue;

            int o0 = Math.Clamp((int)Math.Round((a - startSec) / spanSec * count), 0, count);
            int o1 = Math.Clamp((int)Math.Round((b - startSec) / spanSec * count), 0, count);
            int nOut = o1 - o0;
            if (nOut <= 0) continue;

            double aSec = startSec + o0 * secPerOut;   // timeline second at the first output sample
            if (double.IsNaN(src.NextTimelineSec) || Math.Abs(aSec - src.NextTimelineSec) > ReseekTolSec)
            {
                src.Seek(clip.InPointSec + (aSec - clip.StartSec));   // reposition the file (discontinuity)
                src.NextTimelineSec = aSec;
            }

            int ch = src.Channels;
            EnsureTmp(nOut * ch);
            int got = src.ReadFrames(_tmp, nOut, speed, rate);
            for (int i = 0; i < got; i++)
            {
                float g = GainAt(clip, aSec + i * secPerOut);
                float l = _tmp[i * ch] * g;
                float r = (ch > 1 ? _tmp[i * ch + 1] : _tmp[i * ch]) * g;
                buffers[0][o0 + i] += l;
                if (nch > 1) buffers[1][o0 + i] += r;
            }
            src.NextTimelineSec = aSec + got * secPerOut;   // where this source expects to read next
        }

        _audioPosSec = endSec;
    }

    // Linear fade envelope (× clip gain) at a timeline time in seconds — the smooth, per-sample version
    // of ClipData.GainAt so audio fades don't zipper at frame boundaries.
    private static float GainAt(AudioClipSnap clip, double timelineSec)
    {
        double into = timelineSec - clip.StartSec;
        double left = clip.EndSec - timelineSec;
        float g = clip.Gain;
        if (clip.FadeInSec > 0 && into < clip.FadeInSec) g *= (float)(into / clip.FadeInSec);
        if (clip.FadeOutSec > 0 && left < clip.FadeOutSec) g *= (float)(left / clip.FadeOutSec);
        return g < 0f ? 0f : g;
    }

    private AudioClipSource? EnsureSource(AudioClipSnap clip)
    {
        if (_sources.TryGetValue(clip.Key, out var src))
        {
            if (src.FilePath == clip.FilePath) return src;
            src.Dispose();                 // same clip now points at a different file
            _sources.Remove(clip.Key);
        }
        var created = new AudioClipSource(clip.FilePath);
        _sources[clip.Key] = created;
        return created;
    }

    private void ForceReseekAll()
    {
        foreach (var s in _sources.Values) s.NextTimelineSec = double.NaN;
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

// One clip's audio source: an NAudio file reader played through a live-adjustable resampler (WdlResampler)
// so the mixer can micro-pitch it to chase the playhead. Opened off-thread so the audio thread never
// blocks on IO — until ready (or if the file is missing) it reads as silence. All playback calls
// (Seek/ReadFrames) happen on the audio thread; Open runs once on a Task.
internal sealed class AudioClipSource : IDisposable
{
    public readonly string FilePath;

    private volatile bool _ready;
    private volatile bool _failed;
    private volatile bool _disposeRequested;
    private AudioFileReader? _reader;
    private WdlResampler? _resampler;
    private int _sourceRate = 44100;
    private int _channels = 2;

    // Mixer bookkeeping (audio thread only): the timeline second this source is positioned to read next.
    // NaN means "unknown — reseek before reading".
    public double NextTimelineSec = double.NaN;

    public AudioClipSource(string filePath)
    {
        FilePath = filePath;
        Task.Run(Open);
    }

    private void Open()
    {
        try
        {
            var reader = new AudioFileReader(FilePath);
            _reader = reader;
            _channels = Math.Max(1, reader.WaveFormat.Channels);
            _sourceRate = reader.WaveFormat.SampleRate;

            var r = new WdlResampler();
            r.SetMode(true, 2, false);
            r.SetFilterParms();
            r.SetFeedMode(false);   // output-driven: we ask for N output samples
            _resampler = r;

            _ready = true;
            if (_disposeRequested) DisposeInner();
        }
        catch { _failed = true; }
    }

    public bool Ready => _ready && !_failed;
    public int Channels => _channels;

    // Reposition the underlying file to `fileSec` and flush the resampler.
    public void Seek(double fileSec)
    {
        if (!_ready || _reader is null) return;
        var t = TimeSpan.FromSeconds(Math.Max(0, fileSec));
        if (t > _reader.TotalTime) t = _reader.TotalTime;
        _reader.CurrentTime = t;
        _resampler?.Reset();
    }

    // Produce up to `count` interleaved output frames (engine rate, Channels wide) into dst, playing the
    // file at `speed`× (1.0 = natural). Returns frames actually produced (fewer at end of file). The
    // resampler ratio E/O = speed sets the pitch/speed: output rate O = engineRate / speed.
    public int ReadFrames(float[] dst, int count, double speed, int engineRate)
    {
        if (!Ready || _reader is null || _resampler is null || count <= 0) return 0;
        _resampler.SetRates(_sourceRate, engineRate / speed);
        int inNeeded = _resampler.ResamplePrepare(count, _channels, out float[] inBuf, out int inOff);
        int inRead = _reader.Read(inBuf, inOff, inNeeded * _channels) / _channels;
        return _resampler.ResampleOut(dst, 0, inRead, count, _channels);
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
        _resampler = null;
    }
}
