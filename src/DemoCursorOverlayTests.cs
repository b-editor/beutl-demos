using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests.Demos;

[TestFixture]
public sealed class DemoCursorOverlayTests
{
    [AvaloniaTest]
    [TestCase(1)]
    [TestCase(2)]
    public void Cursor_is_composited_above_an_overlay_popup(double renderScale)
    {
        var target = new Button { Content = "Menu" };
        var window = new Window { Width = 320, Height = 240 };
        var content = new Border { Width = 120, Height = 100, Background = Brushes.Red };
        var popup = new Popup { Child = content, PlacementTarget = target,
            Placement = PlacementMode.Center, ShouldUseOverlayLayer = true };
        window.Content = new Grid { Children = { target, popup } };
        try
        {
            window.Show();
            window.SetRenderScaling(renderScale);
            popup.IsOpen = true;
            HeadlessTestHelpers.Render(3);
            Point point = content.TranslatePoint(new Point(40, 30), window)!.Value;
            using var frame = window.CaptureRenderedFrame()!;
            Assert.That(frame.PixelSize, Is.EqualTo(new PixelSize((int)(320 * renderScale), (int)(240 * renderScale))));
            int x = (int)((point.X + 4) * renderScale), y = (int)((point.Y + 10) * renderScale);
            using (var pixels = frame.Lock())
                Assert.That(Marshal.ReadByte(pixels.Address + y * pixels.RowBytes + x * 4 + 1), Is.Zero,
                    "The test point must be inside the red popup.");
            using var cursor = new DemoCursorOverlay();
            cursor.Draw(frame, new DemoPointerFrame(point.X / 320, point.Y / 240, false));
            using (var pixels = frame.Lock())
                Assert.That(Marshal.ReadByte(pixels.Address + y * pixels.RowBytes + x * 4 + 1), Is.EqualTo(255),
                    "The white cursor must cover the popup, irrespective of Avalonia layer order.");
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    }
}
