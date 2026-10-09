using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Beutl.HeadlessUITests.Demos;

// Frame-counted capture and pointer telemetry share exact frame numbers for the camera pass.
internal sealed class HeadlessVideoRecorder : IAsyncDisposable
{
    private readonly Window _window;
    private readonly DemoVideoEncoder? _encoder;
    private readonly string _outputPath;
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _pixels;
    private readonly PixelFormat _pixelFormat;
    private readonly Func<DemoPointerFrame>? _pointer;
    private readonly List<DemoPointerFrame> _frames = [];
    private readonly DemoCursorOverlay _cursor = new();

    public HeadlessVideoRecorder(Window window, string outputPath, int frameRate, Func<DemoPointerFrame>? pointer = null,
        bool encodeVideo = true)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (frameRate is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(frameRate));
        using WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("The headless editor did not render a frame.");
        _window = window;
        _width = frame.PixelSize.Width;
        _height = frame.PixelSize.Height;
        if (_width % 2 != 0 || _height % 2 != 0)
            throw new InvalidOperationException("H.264 recording requires even window dimensions.");
        using (ILockedFramebuffer pixels = frame.Lock()) _pixelFormat = pixels.Format;
        string format = _pixelFormat == PixelFormat.Bgra8888 ? "bgra"
            : _pixelFormat == PixelFormat.Rgba8888 ? "rgba"
            : throw new NotSupportedException($"Unsupported headless pixel format: {_pixelFormat}");
        FrameRate = frameRate;
        _pixels = encodeVideo ? new byte[checked(_width * _height * 4)] : [];
        _outputPath = Path.GetFullPath(outputPath);
        _pointer = pointer;
        if (encodeVideo) _encoder = new DemoVideoEncoder(outputPath, _width, _height, frameRate, format);
    }

    public int FrameRate { get; }
    public int FrameCount { get; private set; }

    public Task CheckpointAsync(string name) => FrameAsync(Path.Combine(Path.GetDirectoryName(_outputPath)!, name + ".png"));

    public async Task TransitionAsync(double seconds)
    {
        // Menus and expanders are still animating after an input event; do not freeze their first frame.
        for (int i = 0; i < (int)Math.Round(seconds * FrameRate); i++)
            await FrameAsync();
    }

    public async Task FrameAsync(string? stillPath = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        DemoPointerFrame? pointer = _pointer?.Invoke();
        using (WriteableBitmap frame = _window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Headless frame capture returned null."))
        {
            if (frame.PixelSize.Width != _width || frame.PixelSize.Height != _height)
                throw new InvalidOperationException("Window dimensions changed during recording.");
            if (pointer != null) _cursor.Draw(frame, pointer);
            if (stillPath != null) frame.Save(stillPath, PngBitmapEncoderOptions.Default);
            if (_encoder != null)
            {
                using ILockedFramebuffer buffer = frame.Lock();
                if (buffer.Format != _pixelFormat) throw new InvalidOperationException("Pixel format changed during recording.");
                for (int y = 0; y < _height; y++)
                    Marshal.Copy(buffer.Address + y * buffer.RowBytes, _pixels, y * _width * 4, _width * 4);
            }
        }
        if (_encoder != null) await _encoder.WriteAsync(_pixels);
        if (pointer != null) _frames.Add(pointer with { Checkpoint = Path.GetFileNameWithoutExtension(stillPath) });
        FrameCount++;
    }

    public async Task HoldAsync(double seconds)
    {
        int count = (int)Math.Round(seconds * FrameRate);
        if (count == 0) return;
        await FrameAsync();
        // The scenario deliberately holds a settled UI. Reuse that frame instead of repainting it
        // hundreds of times; input, scrubbing, zooming and playback still render every changed frame.
        for (int i = 1; i < count; i++)
        {
            if (_encoder != null) await _encoder.WriteAsync(_pixels);
            if (_pointer?.Invoke() is { } pointer) _frames.Add(pointer with { Checkpoint = null });
            FrameCount++;
        }
    }

    public async Task CompleteAsync()
    {
        if (FrameCount == 0) throw new InvalidOperationException("Cannot publish an empty recording.");
        if (_encoder != null) await _encoder.CompleteAsync();
        if (_pointer != null)
        {
            var recording = new DemoMotionRecording(_width, _height, FrameRate, _frames, _window.RenderScaling);
            await File.WriteAllTextAsync(Path.ChangeExtension(_outputPath, ".motion.json"), JsonSerializer.Serialize(recording));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cursor.Dispose();
        if (_encoder != null) await _encoder.DisposeAsync();
    }
}
