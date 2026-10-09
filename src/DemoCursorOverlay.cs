using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Beutl.HeadlessUITests.Demos;

// Composite after Avalonia has rendered every popup, just like a screen recorder's cursor layer.
internal sealed class DemoCursorOverlay : IDisposable
{
    private readonly SKPath _path = SKPath.ParseSvgPathData("M0,0 L0,23 L6,17 L11,27 L15,25 L10,16 L19,16 Z");
    private readonly SKPaint _fill = new() { Color = SKColors.White, IsAntialias = true };
    private readonly SKPaint _outline = new()
    {
        Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.5f, StrokeJoin = SKStrokeJoin.Round
    };

    public void Draw(WriteableBitmap frame, DemoPointerFrame pointer)
    {
        using ILockedFramebuffer buffer = frame.Lock();
        SKColorType color = buffer.Format == PixelFormat.Bgra8888 ? SKColorType.Bgra8888
            : buffer.Format == PixelFormat.Rgba8888 ? SKColorType.Rgba8888
            : throw new NotSupportedException($"Unsupported cursor pixel format: {buffer.Format}");
        using var surface = SKSurface.Create(new SKImageInfo(frame.PixelSize.Width, frame.PixelSize.Height,
            color, SKAlphaType.Premul), buffer.Address, buffer.RowBytes);
        SKCanvas canvas = surface.Canvas;
        canvas.Translate((float)(pointer.X * frame.PixelSize.Width), (float)(pointer.Y * frame.PixelSize.Height));
        canvas.Scale((float)(frame.Dpi.X / 96), (float)(frame.Dpi.Y / 96));
        canvas.DrawPath(_path, _fill);
        canvas.DrawPath(_path, _outline);
        canvas.Flush();
    }

    public void Dispose()
    {
        _path.Dispose();
        _fill.Dispose();
        _outline.Dispose();
    }
}
