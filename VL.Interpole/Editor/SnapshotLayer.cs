using SkiaSharp;
using Stride.Core.Mathematics;
using VL.Lib.IO.Notifications;
using VL.Skia;

namespace VL.Interpole;

// Identifies one cached page image: the time-axis scroll (in device px), the (quantized) zoom, the
// device size, and a View tag identifying WHAT is drawn (timeline vs. a specific clip's keyframe page).
// Whole-page play-follow makes the scroll recur exactly on loops, so the LRU hits. Keying by scroll (not
// a page index) keeps distinct views distinct even when the page grid is re-anchored; the View tag keeps
// the timeline and a clip page — which share the same time axis / scroll / zoom — from colliding when
// you switch views mid-playback.
public readonly record struct PageKey(int ScrollPx, int PxBucket, int W, int H, int View);

// A caching Skia layer between the editor's SetSpace wrapper and the ToSkiaLayer.
//
// While the timeline plays, the on-screen picture is almost entirely static — only the playhead and
// timecode move — yet ToSkiaLayer re-rasterizes the whole opaque editor onto the window every frame
// (thousands of DrawVertices), a per-pixel cost that scales with the window size and stutters vvvv's
// realtime graphics. So while playing we capture the static UI (per page) into SKImages and each frame
// blit the current page's image (one quad) + let the editor draw ONLY the playhead/timecode overlay on
// top. A small LRU keeps recent pages, so a LOOP re-hits cached pages every lap (no re-capture).
//
// The node drives the mode + CurrentKey each frame:
//   Live         — pass-through to the inner layer (normal editing while stopped).
//   Capture      — render the inner layer (drawn in its Static pass, i.e. no playhead) live to the
//                  screen AND record+rasterize it into the cache under CurrentKey. Used the first time a
//                  page is shown (per zoom/size).
//   BlitOverlay  — blit CurrentKey's cached still, then render the inner layer (drawn in its Overlay
//                  pass, i.e. only playhead + timecode) on top. The steady-state play frame, no raster.
//
// Capture technique: ToSkiaLayer.Render draws PURELY to caller.Canvas (verified by decompiling it), so
// we capture by RECORDING it into an SKPicture and rasterizing that once into an offscreen surface.
//
// Coordinate convention (the crux): the DIPTopLeft caller.Transformation scale = DIPFactor*100 (e.g.
// 175 at 175% DPI). ToSkiaLayer pre-concats Scale(FromImGuiScaling ≈ 1/100), so the NET content matrix
// is M = Transformation·Scale(FromImGuiScaling) ≈ real 1.75x. We rebuild that exact M here so capture
// rasterizes at the true device size (vb*Scaling, not vb*175 → a ~44-gigapixel surface) and the blit
// lands pixel-identical to the live render. The offscreen surface must carry the DESTINATION color
// space or DrawImage darkens the blit. Proven in gamma at 175% DPI.
public sealed class SnapshotLayer : ILayer, IDisposable
{
    private readonly ILayer _inner;
    private const int MaxPages = 8;   // LRU cap; each page ≈ one full window (~15MB GPU at 3686×1073)

    public enum RenderMode { Live, Capture, BlitOverlay }
    public RenderMode Mode = RenderMode.Live;
    public PageKey CurrentKey;

    // Most-recently-used first. _cache holds the images; _lru tracks eviction order.
    private readonly Dictionary<PageKey, SKImage> _cache = new();
    private readonly LinkedList<PageKey> _lru = new();

    public SnapshotLayer(ILayer inner) => _inner = inner;

    public RectangleF? Bounds => _inner.Bounds;

    public bool Notify(INotification notification, CallerInfo caller) => _inner.Notify(notification, caller);

    public bool Has(PageKey key) => _cache.ContainsKey(key);

    public void Invalidate()
    {
        foreach (var img in _cache.Values) img.Dispose();
        _cache.Clear();
        _lru.Clear();
    }

    // The exact matrix ToSkiaLayer draws its content with: Transformation pre-concatenated by
    // Scale(FromImGuiScaling), where FromImGuiScaling = Scaling / Transformation.ScaleX (≈ 1/100).
    private static SKMatrix ContentMatrix(CallerInfo caller)
    {
        var m = caller.Transformation;
        float fromImGui = m.ScaleX != 0 ? caller.Scaling / m.ScaleX : 1f;
        return m.PreConcat(SKMatrix.CreateScale(fromImGui, fromImGui));   // returns a new matrix
    }

    public void Render(CallerInfo caller)
    {
        if (Mode == RenderMode.Live || caller.IsTooltip) { _inner.Render(caller); return; }

        var vb = caller.ViewportBounds;
        var m = ContentMatrix(caller);

        if (Mode == RenderMode.Capture)
        {
            _inner.Render(caller);                 // draw the (static) editor to the screen this frame
            try
            {
                var netRect = m.MapRect(vb);
                int w = Math.Max(1, (int)MathF.Ceiling(netRect.Width));
                int h = Math.Max(1, (int)MathF.Ceiling(netRect.Height));
                Store(CurrentKey, Capture(caller, netRect, w, h));
            }
            catch { /* keep the live render already drawn above */ }
            return;
        }

        // BlitOverlay: blit CurrentKey's cached still, then draw the overlay (playhead + timecode) on top.
        if (!_cache.TryGetValue(CurrentKey, out var img)) { _inner.Render(caller); return; }   // safe fallback
        Touch(CurrentKey);
        var canvas = caller.Canvas;
        canvas.Save();
        canvas.SetMatrix(m);
        canvas.DrawImage(img, new SKRect(0, 0, vb.Width, vb.Height));
        canvas.Restore();
        _inner.Render(caller);                     // the overlay draw data, over the cached still
    }

    private void Store(PageKey key, SKImage img)
    {
        if (_cache.TryGetValue(key, out var old)) { old.Dispose(); _lru.Remove(key); }
        _cache[key] = img;
        _lru.AddFirst(key);
        while (_lru.Count > MaxPages)
        {
            var evict = _lru.Last!.Value;
            _lru.RemoveLast();
            if (_cache.Remove(evict, out var ev)) ev.Dispose();
        }
    }

    private void Touch(PageKey key)
    {
        if (_lru.First is { Value: var f } && f.Equals(key)) return;
        _lru.Remove(key);
        _lru.AddFirst(key);
    }

    // Record the inner layer's draw ops into an SKPicture, then rasterize once into an offscreen surface
    // and return the resulting image.
    private SKImage Capture(CallerInfo caller, SKRect netRect, int w, int h)
    {
        SKPicture picture;
        using (var recorder = new SKPictureRecorder())
        {
            var recCanvas = recorder.BeginRecording(netRect);
            _inner.Render(caller with { Canvas = recCanvas, Surface = null });
            picture = recorder.EndRecording();
        }

        ResolveDestColorSpace(caller);
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul, _destCS);
        SKSurface? surface = caller.GRContext is not null
            ? SKSurface.Create(caller.GRContext, budgeted: true, info)
            : null;
        surface ??= SKSurface.Create(info);

        using (surface)
        using (picture)
        {
            var c = surface.Canvas;
            c.Clear(SKColors.Transparent);
            c.Translate(-netRect.Left, -netRect.Top);   // shift device coords into the surface origin
            c.DrawPicture(picture);
            surface.Flush();
            return surface.Snapshot();
        }
    }

    // The destination surface's color space, resolved once from a screen snapshot, so the cache surface
    // matches it and DrawImage does no color conversion (else the blit darkens).
    private static SKColorSpace? _destCS;
    private static bool _csResolved;
    private static void ResolveDestColorSpace(CallerInfo caller)
    {
        if (_csResolved) return;
        _csResolved = true;
        try { using var ds = caller.Surface?.Snapshot(); _destCS = ds?.ColorSpace; }
        catch { }
    }

    public void Dispose() => Invalidate();
}
