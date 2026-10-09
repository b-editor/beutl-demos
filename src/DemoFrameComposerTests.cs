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
        using var composer = new DemoFrameComposer(1920, 1080, new DemoCameraSettings { Inset = 0.055 });
        composer.Draw(canvas, source, new DemoCameraPose(0.5, 0.5, 1));
        Assert.That(output.GetPixel(10, 1070), Is.Not.EqualTo(SKColors.White), "The overview includes the desktop margin.");

        const double zoom = 1.4;
        composer.Draw(canvas, source, new DemoCameraPose(1 - 0.5 / zoom, 0.5 / zoom, zoom));
        Assert.That(output.GetPixel(10, 1070), Is.EqualTo(SKColors.White),
            "The lower-left corner must show the enlarged UI, not a fixed wallpaper border.");
    }
}
