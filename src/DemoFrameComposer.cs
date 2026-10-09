using SkiaSharp;

namespace Beutl.HeadlessUITests.Demos;

// Stills and video use this same compositor, so an approved preview fixes the export's appearance.
internal sealed class DemoFrameComposer : IDisposable
{
    private readonly SKBitmap _background;
    private readonly SKBitmap? _titleBar;
    private readonly SKRect _content;
    private readonly SKRect _title;
    private readonly SKRoundRect _clip;
    private readonly SKPaint _border = new() { Color = new SKColor(255, 255, 255, 35), IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKSamplingOptions _sampling = new(SKCubicResampler.CatmullRom);
    private readonly int _width;
    private readonly int _height;

    public DemoFrameComposer(int width, int height, DemoCameraSettings settings)
    {
        settings.Validate();
        _width = width;
        _height = height;
        _titleBar = settings.TitleBarPath == null ? null : Load(settings.TitleBarPath);
        float titleHeight = _titleBar == null ? 0 : _titleBar.Height * width / (float)_titleBar.Width;
        float scale = Math.Min(width * (1 - 2 * (float)settings.Inset) / width,
            height * (1 - 2 * (float)settings.Inset) / (height + titleHeight));
        float outerWidth = width * scale;
        float outerHeight = (height + titleHeight) * scale;
        var window = SKRect.Create((width - outerWidth) / 2, (height - outerHeight) / 2, outerWidth, outerHeight);
        _title = new SKRect(window.Left, window.Top, window.Right, window.Top + titleHeight * scale);
        _content = new SKRect(window.Left, _title.Bottom, window.Right, window.Bottom);
        float radius = (_titleBar == null ? 18 : 12) * height / 1080f;
        _clip = new SKRoundRect(window, radius, radius);
        _background = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var backdrop = new SKCanvas(_background);
        if (settings.WallpaperPath is { } wallpaperPath)
        {
            using SKBitmap wallpaper = Load(wallpaperPath);
            float fill = Math.Max(width / (float)wallpaper.Width, height / (float)wallpaper.Height);
            float cropWidth = width / fill, cropHeight = height / fill;
            var crop = SKRect.Create((wallpaper.Width - cropWidth) / 2, (wallpaper.Height - cropHeight) / 2, cropWidth, cropHeight);
            backdrop.DrawBitmap(wallpaper, crop, SKRect.Create(width, height), _sampling);
        }
        else backdrop.Clear(new SKColor(30, 33, 43));
        using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 18 * height / 1080f);
        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 140), MaskFilter = blur, IsAntialias = true };
        var shadowRect = window;
        shadowRect.Offset(0, 10 * height / 1080f);
        backdrop.DrawRoundRect(shadowRect, radius, radius, shadow);
    }

    public DemoPointerFrame[] DesktopFrames(IReadOnlyList<DemoPointerFrame> frames) => frames.Select(frame =>
    {
        DemoCameraTarget point = Map(frame.X, frame.Y);
        return frame with
        {
            X = point.X, Y = point.Y,
            CameraTarget = frame.CameraTarget is { } target ? Map(target.X, target.Y) : null
        };
    }).ToArray();

    private DemoCameraTarget Map(double x, double y) => new(
        (_content.Left + x * _content.Width) / _width, (_content.Top + y * _content.Height) / _height);

    public void Draw(SKCanvas canvas, SKBitmap source, DemoCameraPose pose)
    {
        // Move the entire desktop together. Cropping only the editor inside a fixed window leaves
        // wallpaper visible in the opposite corner even when that corner should be out of view.
        canvas.Save();
        canvas.Translate(_width / 2f, _height / 2f);
        canvas.Scale((float)pose.Zoom);
        canvas.Translate((float)(-pose.X * _width), (float)(-pose.Y * _height));
        canvas.DrawBitmap(_background, 0, 0, SKSamplingOptions.Default);
        canvas.Save();
        canvas.ClipRoundRect(_clip, antialias: true);
        if (_titleBar != null) canvas.DrawBitmap(_titleBar, _title, _sampling);
        // Keep the high-DPI source intact until this final camera transform and downsample.
        canvas.DrawBitmap(source, _content, _sampling);
        canvas.Restore();
        canvas.DrawRoundRect(_clip, _border);
        canvas.Restore();
    }

    private static SKBitmap Load(string path) => SKBitmap.Decode(path)
        ?? throw new InvalidDataException($"Could not decode presentation asset: {path}");

    public void Dispose()
    {
        _background.Dispose();
        _titleBar?.Dispose();
        _clip.Dispose();
        _border.Dispose();
    }
}
