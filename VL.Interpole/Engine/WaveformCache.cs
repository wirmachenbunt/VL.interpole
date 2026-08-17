using System.Collections.Concurrent;
using NAudio.Wave;

namespace VL.Interpole.Engine;

// Waveform peak data for one audio file: min/max envelope pairs computed over fixed-size buckets of
// source frames, plus the file's rate and duration. This is the *display* representation only — the
// audio mixer reads the file itself for playback; the editor reads these peaks to draw the clip.
// Immutable once built, so it can be shared across clips and read from the render thread safely.
public sealed class WaveformPeaks
{
    public readonly float[] Min;              // per-bucket minimum of the mono-mixed signal (~[-1,1])
    public readonly float[] Max;              // per-bucket maximum
    public readonly int SampleRate;           // source frames per second
    public readonly int SamplesPerBucket;     // source frames summarised by each Min/Max pair
    public readonly double DurationSeconds;

    public WaveformPeaks(float[] min, float[] max, int sampleRate, int samplesPerBucket, double durationSeconds)
    {
        Min = min; Max = max;
        SampleRate = sampleRate;
        SamplesPerBucket = samplesPerBucket;
        DurationSeconds = durationSeconds;
    }

    // Min/max of the signal over the file-time window [t0, t1) seconds — the envelope one screen
    // pixel column should draw. Returns false when the window falls entirely outside the file (so the
    // caller can leave that column blank). t0/t1 are clamped into range; a zero-width window reads the
    // single bucket at t0.
    public bool Envelope(double t0, double t1, out float min, out float max)
    {
        min = 0f; max = 0f;
        if (Min.Length == 0) return false;
        if (t1 < t0) (t0, t1) = (t1, t0);
        if (t1 < 0 || t0 > DurationSeconds) return false;

        double perBucketSec = (double)SamplesPerBucket / SampleRate;
        int b0 = (int)(t0 / perBucketSec);
        int b1 = (int)(t1 / perBucketSec);
        b0 = Math.Clamp(b0, 0, Min.Length - 1);
        b1 = Math.Clamp(b1, 0, Min.Length - 1);

        float mn = float.MaxValue, mx = float.MinValue;
        for (int b = b0; b <= b1; b++) { if (Min[b] < mn) mn = Min[b]; if (Max[b] > mx) mx = Max[b]; }
        min = mn; max = mx;
        return true;
    }
}

public enum WaveformState { Loading, Ready, Failed }

// A process-wide cache of decoded waveform peaks, keyed by absolute file path. Decoding a file to
// peaks is done once, off the render thread (a Task), and the result is shared by every clip that
// references that path. Entries are invalidated when the file's last-write time changes on disk.
// Peaks are deliberately NOT persisted in the document (they'd bloat the JSON) — they are recomputed
// the first time a clip is drawn after a reload.
public static class WaveformCache
{
    // 256 source frames per envelope pair — ~5 ms at 48 kHz. Fine detail for authoring without
    // making the arrays huge (a 5-minute file ≈ 56k pairs ≈ 450 KB).
    private const int SamplesPerBucket = 256;

    private sealed class Entry
    {
        public long Stamp;                       // file last-write-time ticks the peaks were built from
        public volatile WaveformState State;
        public volatile WaveformPeaks? Peaks;
    }

    private static readonly ConcurrentDictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);

    // Current state of the peaks for `path`, kicking off a background decode on first request (or when
    // the file has changed on disk). `peaks` is non-null only when the state is Ready. Safe to call
    // every frame from the render thread — it just reads cached state after the first call.
    public static WaveformState Get(string path, out WaveformPeaks? peaks)
    {
        peaks = null;
        if (string.IsNullOrWhiteSpace(path)) return WaveformState.Failed;

        long stamp = SafeStamp(path);
        var entry = Cache.GetOrAdd(path, p => StartLoad(p, stamp));
        // File changed on disk since we cached it → rebuild.
        if (entry.Stamp != stamp && entry.State != WaveformState.Loading)
        {
            entry = StartLoad(path, stamp);
            Cache[path] = entry;
        }

        peaks = entry.Peaks;
        return entry.State;
    }

    // Read just the file's duration (opens the file, reads its header — no full decode). Used to size a
    // freshly dropped clip to the file length. Returns 0 on any failure.
    public static double ProbeDurationSeconds(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            return reader.TotalTime.TotalSeconds;
        }
        catch { return 0; }
    }

    private static Entry StartLoad(string path, long stamp)
    {
        var entry = new Entry { Stamp = stamp, State = WaveformState.Loading, Peaks = null };
        Task.Run(() =>
        {
            try
            {
                entry.Peaks = Decode(path);
                entry.State = WaveformState.Ready;
            }
            catch
            {
                entry.State = WaveformState.Failed;
            }
        });
        return entry;
    }

    private static WaveformPeaks Decode(string path)
    {
        using var reader = new AudioFileReader(path);
        int ch = Math.Max(1, reader.WaveFormat.Channels);
        int sr = reader.WaveFormat.SampleRate;
        double dur = reader.TotalTime.TotalSeconds;

        var min = new List<float>();
        var max = new List<float>();
        var buf = new float[ch * 8192];        // interleaved float samples

        int frameInBucket = 0;
        float mn = float.MaxValue, mx = float.MinValue;
        int read;
        while ((read = reader.Read(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i + ch <= read; i += ch)
            {
                float s = 0f;
                for (int c = 0; c < ch; c++) s += buf[i + c];
                s /= ch;                       // mono mix-down for the display envelope
                if (s < mn) mn = s;
                if (s > mx) mx = s;
                if (++frameInBucket >= SamplesPerBucket)
                {
                    min.Add(mn); max.Add(mx);
                    mn = float.MaxValue; mx = float.MinValue;
                    frameInBucket = 0;
                }
            }
        }
        if (frameInBucket > 0) { min.Add(mn); max.Add(mx); }

        return new WaveformPeaks(min.ToArray(), max.ToArray(), sr, SamplesPerBucket, dur);
    }

    private static long SafeStamp(string path)
    {
        try { return File.GetLastWriteTimeUtc(path).Ticks; }
        catch { return 0; }
    }
}
