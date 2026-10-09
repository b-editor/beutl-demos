using SkiaSharp;

namespace Beutl.HeadlessUITests.Demos;

[TestFixture]
public sealed class DemoFrameComposerTests
{
    [Test]
    public void Zoom_toward_upper_right_moves_the_opposite_window_corner_out_of_view()
    {
        using var source = new SKBitmap(3840, 2160);
        source.Erase(SKColors.White);
        using var output = new SKBitmap(1920, 1080);
        using var canvas = new SKCanvas(output);
        using var composer = new DemoFrameComposer(1920, 1080, source.Width, source.Height, new DemoCameraSettings { Inset = 0.055 });
        composer.Draw(canvas, source, new DemoCameraPose(0.5, 0.5, 1));
        Assert.That(output.GetPixel(10, 1070), Is.Not.EqualTo(SKColors.White), "The overview includes the desktop margin.");

        const double zoom = 1.4;
        composer.Draw(canvas, source, new DemoCameraPose(1 - 0.5 / zoom, 0.5 / zoom, zoom));
        Assert.That(output.GetPixel(10, 1070), Is.EqualTo(SKColors.White),
            "The lower-left corner must show the enlarged UI, not a fixed wallpaper border.");
    }

    [Test]
    public void Retina_window_keeps_its_aspect_ratio_inside_a_widescreen_video()
    {
        string titlePath = Path.GetTempFileName();
        try
        {
            // Together these form a 1470x956 logical window at 2x DPI.
            using var title = new SKBitmap(2940, 80);
            title.Erase(SKColors.Magenta);
            using (SKData png = title.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(titlePath, png.ToArray());
            using var source = new SKBitmap(2940, 1832);
            source.Erase(SKColors.White);
            using (var sourceCanvas = new SKCanvas(source))
            using (var paint = new SKPaint { Color = SKColors.Blue })
                sourceCanvas.DrawRect(SKRect.Create(1370, 816, 200, 200), paint);
            using var output = new SKBitmap(1920, 1080);
            using var canvas = new SKCanvas(output);
            using var composer = new DemoFrameComposer(1920, 1080, source.Width, source.Height,
                new DemoCameraSettings { Inset = 0.055, TitleBarPath = titlePath });
            composer.Draw(canvas, source, new DemoCameraPose(0.5, 0.5, 1));

            int left = output.Width, right = -1, top = output.Height, bottom = -1;
            int blueLeft = output.Width, blueRight = -1, blueTop = output.Height, blueBottom = -1;
            for (int y = 0; y < output.Height; y++)
            for (int x = 0; x < output.Width; x++)
            {
                SKColor color = output.GetPixel(x, y);
                if (color == SKColors.White || color == SKColors.Magenta || color == SKColors.Blue)
                {
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
                if (color == SKColors.Blue)
                {
                    blueLeft = Math.Min(blueLeft, x); blueRight = Math.Max(blueRight, x);
                    blueTop = Math.Min(blueTop, y); blueBottom = Math.Max(blueBottom, y);
                }
            }
            Assert.That((right - left + 1d) / (bottom - top + 1), Is.EqualTo(1470d / 956).Within(0.005),
                "The title bar and content must retain the MacBook Air window's combined aspect ratio.");
            Assert.That(blueRight - blueLeft + 1, Is.GreaterThan(90));
            Assert.That(blueRight - blueLeft, Is.EqualTo(blueBottom - blueTop).Within(1),
                "A square in the captured UI must remain square, rather than stretching to 16:9.");
        }
        finally { File.Delete(titlePath); }
    }
}
