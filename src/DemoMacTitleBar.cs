using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Beutl.Testing.Headless;
using Beutl.Views;
using SkiaSharp;

namespace Beutl.HeadlessUITests.Demos;

internal static class DemoMacTitleBar
{
    public static void Capture(string path, int width, double renderScale = 2)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Capture the production macOS title bar on macOS.");
        // Host just the production title row. Attaching MainView itself would also run app-startup tasks.
        var shell = new MainView();
        var root = (Grid)shell.Content!;
        Grid title = shell.FindControl<Grid>("Titlebar")!;
        Border separator = root.Children.OfType<Border>().Single(c => Grid.GetRow(c) == 1);
        root.Children.Remove(title);
        root.Children.Remove(separator);
        var host = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,1"),
            DataContext = TestShell.MainViewModel,
            Children = { title, separator }
        };
        Assert.That(Application.Current!.TryFindResource("MainWindowBackground", ThemeVariant.Dark, out object? background), Is.True);
        var window = new Window
        {
            Width = width, Height = title.Height + 1, Content = host,
            Background = (IBrush)background!, RequestedThemeVariant = ThemeVariant.Dark
        };
        try
        {
            window.Show();
            window.SetRenderScaling(renderScale);
            HeadlessTestHelpers.Render(3);
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Title bar did not render.");
            frame.Save(path, PngBitmapEncoderOptions.Default);
            // AppKit's traffic lights are outside Avalonia's headless framebuffer.
            // Everything else, including the breadcrumb and actions, comes from MainView's current XAML.
            using SKBitmap bitmap = SKBitmap.Decode(path);
            using var canvas = new SKCanvas(bitmap);
            canvas.Scale((float)renderScale);
            using var fill = new SKPaint { IsAntialias = true };
            using var border = new SKPaint { Color = new SKColor(0, 0, 0, 65), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.75f };
            SKColor[] colors = [new(255, 95, 87), new(254, 188, 46), new(40, 200, 64)];
            for (int i = 0; i < colors.Length; i++)
            {
                fill.Color = colors[i];
                canvas.DrawCircle(20 + i * 20, (float)title.Height / 2, 6, fill);
                canvas.DrawCircle(20 + i * 20, (float)title.Height / 2, 6, border);
            }
            using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, png.ToArray());
        }
        finally
        {
            window.Close();
            host.DataContext = null;
        }
    }
}
