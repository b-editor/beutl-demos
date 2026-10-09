using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Testing.Headless;
using GraphScope = Beutl.HeadlessUITests.GraphEditorContextMenuTests.GraphScope;

namespace Beutl.HeadlessUITests.Demos;

[TestFixture]
public sealed class DemoGraphFramingTests
{
    [AvaloniaTest]
    [TestCase(false, 0)]
    [TestCase(true, 0)]
    [TestCase(false, 2.04)]
    [TestCase(true, 2.04)]
    public async Task Fit_keeps_keys_inside_the_laptop_plot(bool autoHeight, double elementStart)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true, selectAll: false);
        graph.Window.Width = 1200;
        graph.Window.Height = 425.5;
        graph.Model.Scene.Duration = TimeSpan.FromSeconds(9);
        graph.Model.Element!.Start = TimeSpan.FromSeconds(elementStart);
        graph.Model.Element.Length = TimeSpan.FromSeconds(2.45);
        graph.First.KeyTime = TimeSpan.Zero;
        graph.Second.KeyTime = TimeSpan.FromSeconds(2.4);
        graph.Second.Value = 475;
        graph.Animation.KeyFrames.Insert(1, new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1.5), Value = 100 });
        graph.Model.Options.Value = graph.Model.Options.Value with { Scale = 0.85f };
        // Dock tabs remain mounted while hidden. The inactive timeline must not write
        // an offset clamped against its old extent over the active graph's fit result.
        using var timelineModel = new TimelineTabViewModel(graph.Model.EditorContext);
        var timeline = new TimelineTabView { DataContext = timelineModel };
        graph.Window.Content = null;
        // Dock switches can detach a graph while its model subscriptions remain alive.
        // Its stale viewport must not refit or coerce the active view's shared offset.
        var retired = new GraphEditorView { DataContext = graph.Model };
        graph.Window.Content = retired;
        HeadlessTestHelpers.Render(3);
        graph.Window.Content = new Grid { Children = { graph.View, timeline } };
        HeadlessTestHelpers.Render(3);
        timeline.IsVisible = false;
        HeadlessTestHelpers.Render(3);
        if (autoHeight)
        {
            var height = graph.View.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
                .Single(b => Equals(b.Tag, "AutoHeight"));
            Point heightPoint = height.TranslatePoint(new Point(height.Bounds.Width / 2, height.Bounds.Height / 2), graph.Window)!.Value;
            graph.Window.MouseMove(heightPoint);
            graph.Window.MouseDown(heightPoint, MouseButton.Left);
            graph.Window.MouseUp(heightPoint, MouseButton.Left);
            HeadlessTestHelpers.Render(3);
            Assert.That(graph.Model.AutoZoomHeight.Value, Is.True);
        }
        Button fit = graph.View.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "FitAll"));
        Point point = fit.TranslatePoint(new Point(fit.Bounds.Width / 2, fit.Bounds.Height / 2), graph.Window)!.Value;
        graph.Window.MouseMove(point);
        graph.Window.MouseDown(point, MouseButton.Left);
        graph.Window.MouseUp(point, MouseButton.Left);
        HeadlessTestHelpers.Render(3);
        var scroll = graph.View.FindControl<ScrollViewer>("scroll")!;
        TestContext.Progress.WriteLine($"Fit: auto={autoHeight}, scale={graph.Model.Options.Value.Scale}, offset={scroll.Offset}, margin={graph.Model.Margin.Value}, extent={scroll.Extent}, viewport={scroll.Viewport}");
        foreach (var key in graph.Animation.KeyFrames)
        {
            Point center = graph.KeyFrame(key).TranslatePoint(default, scroll)!.Value;
            Assert.That(center.X, Is.InRange(0, scroll.Viewport.Width - 16), $"Key at {key.KeyTime} must fit horizontally.");
            Assert.That(center.Y, Is.InRange(16, scroll.Viewport.Height - 16));
        }
        graph.Capture($"fit-laptop-offset-{elementStart}-{autoHeight}");
    }
}
