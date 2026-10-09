using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
        var content = new Border { Width = 120, Height = 100, Background = Brushes.Red,
            Cursor = new Cursor(StandardCursorType.Cross) };
        var popup = new Popup { Child = content, PlacementTarget = target,
            Placement = PlacementMode.Center, ShouldUseOverlayLayer = true };
        window.Content = new Grid { Children = { target, popup } };
        var state = new DemoCursorState(window);
        try
        {
            window.Show();
            window.SetRenderScaling(renderScale);
            popup.IsOpen = true;
            HeadlessTestHelpers.Render(3);
            Point point = content.TranslatePoint(new Point(40, 30), window)!.Value;
            window.MouseMove(point);
            Assert.That(state.At(point), Is.EqualTo("Cross"), "A popup's cursor must win over the window below it.");
            using var frame = window.CaptureRenderedFrame()!;
            Assert.That(frame.PixelSize, Is.EqualTo(new PixelSize((int)(320 * renderScale), (int)(240 * renderScale))));
            var region = new PixelRect((int)((point.X - 8) * renderScale), (int)((point.Y - 8) * renderScale),
                (int)(40 * renderScale), (int)(52 * renderScale));
            Assert.That(CountMonochrome(frame, region), Is.EqualTo((0, 0)), "The region must be inside the red popup.");
            using var cursor = new DemoCursorOverlay();
            cursor.Draw(frame, new DemoPointerFrame(point.X / 320, point.Y / 240, false, Cursor: "None"));
            Assert.That(CountMonochrome(frame, region), Is.EqualTo((0, 0)), "None must remain invisible.");
            cursor.Draw(frame, new DemoPointerFrame(point.X / 320, point.Y / 240, false));
            var (black, white) = CountMonochrome(frame, region);
            Assert.That(black, Is.GreaterThan(10 * renderScale * renderScale), "The native black arrow must cover the popup.");
            Assert.That(white, Is.GreaterThan(8 * renderScale * renderScale), "The arrow's white outline must remain visible.");
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Cursor_follows_inheritance_and_keeps_the_captured_drag_shape()
    {
        var child = new Border { Background = Brushes.Red };
        var label = new Border { Width = 120, Height = 100, Child = child,
            Cursor = new Cursor(StandardCursorType.SizeWestEast) };
        var text = new TextBox { Width = 120, Height = 100, Text = "Edit text" };
        Canvas.SetLeft(text, 160);
        var window = new Window { Width = 320, Height = 240,
            Content = new Canvas { Children = { label, text } } };
        var state = new DemoCursorState(window);
        label.PointerPressed += (_, e) => e.Pointer.Capture(label);
        label.PointerReleased += (_, e) => e.Pointer.Capture(null);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var start = new Point(30, 40);
            var outside = new Point(210, 40);
            window.MouseMove(start);
            Assert.That(state.At(start), Is.EqualTo("SizeWestEast"), "Child content inherits its parent's cursor.");
            window.MouseDown(start, MouseButton.Left);
            label.Cursor = new Cursor(StandardCursorType.Hand);
            window.MouseMove(outside);
            Assert.That(state.At(outside), Is.EqualTo("Hand"), "A captured drag retains its updated cursor outside the label.");
            window.MouseUp(outside, MouseButton.Left);
            window.MouseMove(outside);
            Assert.That(state.At(outside), Is.EqualTo("Ibeam"), "Releasing the drag restores the text field's cursor.");
        }
        finally { window.Close(); }
    }

    private static (int Black, int White) CountMonochrome(WriteableBitmap frame, PixelRect region)
    {
        int black = 0, white = 0;
        using var pixels = frame.Lock();
        for (int y = region.Y; y < region.Bottom; y++)
        for (int x = region.X; x < region.Right; x++)
        {
            IntPtr pixel = pixels.Address + y * pixels.RowBytes + x * 4;
            int a = Marshal.ReadByte(pixel), b = Marshal.ReadByte(pixel + 1), c = Marshal.ReadByte(pixel + 2);
            if (a < 30 && b < 30 && c < 30) black++;
            if (a > 220 && b > 220 && c > 220) white++;
        }
        return (black, white);
    }
}
