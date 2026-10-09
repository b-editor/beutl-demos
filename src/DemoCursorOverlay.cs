using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Beutl.HeadlessUITests.Demos;

internal sealed record DemoCursorImage(string File, double Width, double Height, double HotspotX, double HotspotY);
internal sealed record DemoCursorManifest(double Scale, Dictionary<string, DemoCursorImage> Cursors);

// Composite native AppKit artwork after every Avalonia popup, using its real click hotspot.
internal sealed class DemoCursorOverlay : IDisposable
{
    private readonly string _directory;
    private readonly DemoCursorManifest _manifest;
    private readonly Dictionary<string, SKImage> _images = [];
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.CatmullRom);

    public DemoCursorOverlay()
    {
        _directory = Environment.GetEnvironmentVariable("BEUTL_DEMO_CURSOR_DIR")
            ?? throw new InvalidOperationException("Run scripts/demo.py to export the host's native macOS cursors first.");
        _manifest = JsonSerializer.Deserialize<DemoCursorManifest>(File.ReadAllText(Path.Combine(_directory, "cursors.json")))
            ?? throw new InvalidDataException("Native cursor manifest is missing.");
        foreach (var entry in _manifest.Cursors.Values.DistinctBy(c => c.File))
        {
            SKImage image = SKImage.FromEncodedData(Path.Combine(_directory, entry.File))
                ?? throw new InvalidDataException($"Cannot load cursor {entry.File}.");
            if (image.Width != Math.Ceiling(entry.Width * _manifest.Scale)
                || image.Height != Math.Ceiling(entry.Height * _manifest.Scale))
                throw new InvalidDataException($"Cursor {entry.File} does not match its native scale.");
            _images.Add(entry.File, image);
        }
    }

    public void SaveAssets(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (string file in _images.Keys.Append("cursors.json"))
            File.Copy(Path.Combine(_directory, file), Path.Combine(directory, file));
    }

    public void Draw(WriteableBitmap frame, DemoPointerFrame pointer)
    {
        if (pointer.Cursor == "None") return;
        // Standard cursors use their native names. A custom bitmap can safely fall back to Arrow.
        DemoCursorImage entry = _manifest.Cursors.GetValueOrDefault(pointer.Cursor) ?? _manifest.Cursors["Arrow"];
        using ILockedFramebuffer buffer = frame.Lock();
        SKColorType color = buffer.Format == PixelFormat.Bgra8888 ? SKColorType.Bgra8888
            : buffer.Format == PixelFormat.Rgba8888 ? SKColorType.Rgba8888
            : throw new NotSupportedException($"Unsupported cursor pixel format: {buffer.Format}");
        using var surface = SKSurface.Create(new SKImageInfo(frame.PixelSize.Width, frame.PixelSize.Height,
            color, SKAlphaType.Premul), buffer.Address, buffer.RowBytes);
        float scaleX = (float)(frame.Dpi.X / 96), scaleY = (float)(frame.Dpi.Y / 96);
        float left = (float)(pointer.X * frame.PixelSize.Width - entry.HotspotX * scaleX);
        float top = (float)(pointer.Y * frame.PixelSize.Height - entry.HotspotY * scaleY);
        surface.Canvas.DrawImage(_images[entry.File],
            SKRect.Create(left, top, (float)entry.Width * scaleX, (float)entry.Height * scaleY), Sampling);
        surface.Canvas.Flush();
    }

    public void Dispose()
    {
        foreach (SKImage image in _images.Values) image.Dispose();
    }
}
