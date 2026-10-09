using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Automation;
using Avalonia.Input.Raw;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components;
using Beutl.Editor.Components.LibraryTab.Views;
using Beutl.Editor.Components.LibraryTab.Views.LibraryViews;
using Avalonia.VisualTree;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.Helpers;
using Beutl.Extensibility;
using Beutl.Controls.PropertyEditors;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Effects;
using Beutl.Services;
using Avalonia.Styling;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Dock.Avalonia.Controls;
using Beutl.ViewModels.Editors;
using Beutl.Views;
using Beutl.Views.Editors;
using Path = Avalonia.Controls.Shapes.Path;

namespace Beutl.HeadlessUITests.Demos;

// The scenario addresses scene objects and editor services. Only this adapter knows UI controls.
// Pointer targets are resolved from the current visual tree, never stored screen coordinates.
internal readonly record struct DemoAdjustment(float Value, double Seconds);

internal sealed class DemoEditor
{
    private readonly EditViewModel _editor;
    private readonly GraphEditorTabViewModel _graph;
    private Point _pointer = new(700, 380);
    private bool _focusCamera;
    private DemoCameraTarget? _cameraTarget;
    private string? _instruction;

    public DemoEditor(EditViewModel editor, int width = 1920, int height = 1080, double renderScale = 2)
    {
        _editor = editor;
        Window = new Window
        {
            Width = width,
            Height = height,
            Content = new EditView { DataContext = editor }
        };
        // Flyout hosts must render into the captured headless window.
        Window.Styles.Add(new Style(s => s.OfType<Popup>())
        {
            Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) }
        });
        MovePointer(_pointer);
        Window.Show();
        Window.SetRenderScaling(renderScale);
        HeadlessTestHelpers.Render(3);
        Assert.That(Window.ClientSize, Is.EqualTo(new Size(width, height)), "DPI must not change the logical UI layout.");
        Assert.That(Window.RenderScaling, Is.EqualTo(renderScale));
        Timeline.Options.Value = Timeline.Options.Value with { Scale = 0.85f };
        // Keep one reusable graph in the bottom dock, like a workspace arranged before recording.
        _graph = new GraphEditorTabViewModel(editor);
        _editor.DockHost.OpenToolTab(_graph, _editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Bottom));
        _editor.OpenToolTab(Timeline);
        HeadlessTestHelpers.Render(2);
    }

    public Window Window { get; }
    public TimelineTabViewModel Timeline => _editor.FindToolTab<TimelineTabViewModel>()
        ?? throw new InvalidOperationException("The editor's Timeline tab is missing.");

    public Element Element(string name) => _editor.Scene.Children.Single(e => e.Name == name);

    public ValueTask<bool> SaveAsync() => _editor.SaveAsync();

    public void Describe(string instruction)
    {
        _instruction = instruction;
        HeadlessTestHelpers.Render();
    }

    public async Task OverviewAsync(HeadlessVideoRecorder recorder, double seconds = 0.7)
    {
        _focusCamera = false;
        await recorder.HoldAsync(seconds);
    }

    public DemoPointerFrame CapturePointer() => new(
        Math.Clamp(_pointer.X / Window.ClientSize.Width, 0, 1),
        Math.Clamp(_pointer.Y / Window.ClientSize.Height, 0, 1), _focusCamera && !_graph.IsSelected.Value,
        Caption: _instruction, CameraTarget: _cameraTarget);

    public async Task FramePropertyAsync(Guid owner, string name, HeadlessVideoRecorder recorder)
    {
        Control control = PropertyMenu(owner, name);
        await RevealInspectorControlAsync(control, recorder);
        await FrameControlAsync(control, recorder);
    }

    private async Task FrameControlAsync(Control control, HeadlessVideoRecorder recorder)
    {
        Point center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)!.Value;
        _cameraTarget = new DemoCameraTarget(Math.Clamp(center.X / Window.ClientSize.Width, 0, 1),
            Math.Clamp(center.Y / Window.ClientSize.Height, 0, 1));
        _focusCamera = true;
        await recorder.HoldAsync(0.6);
    }

    public async Task SeekAsync(double seconds)
    {
        HeadlessTestHelpers.Settle();
        await RenderThread.Dispatcher.InvokeAsync(() => { });
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = _editor.Player.AfterRendered.Subscribe(_ => rendered.TrySetResult());
        _editor.Player.CurrentFrame.Value = TimeSpan.FromSeconds(seconds);
        _editor.Player.QueuePreviewRender();
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        if (_editor.Player.PreviewRenderError.Value is { } error) throw new InvalidOperationException(error);
        HeadlessTestHelpers.Render();
    }

    public async Task PlayAsync(HeadlessVideoRecorder recorder, double start, double end)
    {
        _focusCamera = false;
        await recorder.HoldAsync(0.6);
        int count = (int)Math.Round((end - start) * recorder.FrameRate);
        for (int frame = 0; frame < count; frame++)
        {
            // Advance the real player clock and await its renderer; recording never races real-time playback.
            await SeekAsync(start + (double)frame / recorder.FrameRate);
            await recorder.FrameAsync();
        }
    }

    public async Task SelectAsync(string name, HeadlessVideoRecorder recorder)
    {
        await ShowTimelineAsync(recorder);
        if (_graph.Element.Value != Element(name))
        {
            _graph.Element.Value = null;
            _graph.Select(null);
        }
        HeadlessTestHelpers.Render();
        Element element = Element(name);
        ElementView row = Window.GetVisualDescendants().OfType<ElementView>()
            .Single(v => v.DataContext is ElementViewModel model && model.Model == element);
        // ElementView stretches across the entire row; its named border is the actual clip hit target.
        Border target = row.FindControl<Border>("border")
            ?? throw new InvalidOperationException($"Timeline element '{name}' has no clip border.");
        target.BringIntoView();
        HeadlessTestHelpers.Render(2);
        Point destination = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException($"Timeline element '{name}' is not attached.");
        if (!new Rect(Window.ClientSize).Contains(destination))
            throw new InvalidOperationException($"Timeline element '{name}' did not scroll into view.");
        await ClickAsync(target, recorder);
        var selection = (IEditorSelection)_editor.GetService(typeof(IEditorSelection))!;
        if (selection.SelectedObject.Value != element)
            throw new InvalidOperationException($"Clicking timeline element '{name}' did not select it.");
    }

    public async Task PointAtAsync(Control target, HeadlessVideoRecorder recorder, Point? position = null)
    {
        // Encoding can take longer than the pointer's recorded travel time. Keep wall-clock
        // tooltip timers from opening another popup over a target before its recorded click.
        ToolTip.SetShowDelay(target, int.MaxValue);
        ToolTip.SetBetweenShowDelay(target, -1);
        ToolTip.SetIsOpen(target, false);
        await RevealInspectorControlAsync(target, recorder);
        HeadlessTestHelpers.Render(2);
        Point destination = target.TranslatePoint(position ?? new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException("The pointer target is not attached.");
        if (!new Rect(Window.ClientSize).Contains(destination))
            throw new InvalidOperationException("The pointer target did not scroll into view.");
        await MoveToAsync(destination, recorder);
    }

    private async Task RevealInspectorControlAsync(Control target, HeadlessVideoRecorder recorder)
    {
        var inspector = target.GetVisualAncestors().OfType<ElementPropertyTabView>().FirstOrDefault();
        if (inspector == null) return;
        ScrollViewer scroll = inspector.FindControl<ScrollViewer>("scrollViewer")!;
        Point origin = scroll.TranslatePoint(default, Window)!.Value;
        bool moved = false;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            HeadlessTestHelpers.Render(2);
            double y = target.TranslatePoint(new Point(0, target.Bounds.Height / 2), Window)!.Value.Y;
            double delta = y - Math.Clamp(y, origin.Y + 24, origin.Y + scroll.Bounds.Height - 24);
            if (Math.Abs(delta) < 1) return;
            if (!moved)
            {
                await MoveToAsync(origin + new Avalonia.Vector(scroll.Bounds.Width * 0.72, scroll.Bounds.Height * 0.55), recorder);
                moved = true;
            }
            Window.MouseWheel(_pointer, new Avalonia.Vector(0, -Math.Sign(delta) * 2));
            await recorder.TransitionAsync(0.2);
        }
        throw new InvalidOperationException("The inspector field could not be reached by scrolling.");
    }

    private async Task MoveToAsync(Point destination, HeadlessVideoRecorder recorder)
    {
        Point from = _pointer;
        double distance = Math.Sqrt(Math.Pow(destination.X - from.X, 2) + Math.Pow(destination.Y - from.Y, 2));
        if (distance < 0.5) return;
        double travelSeconds = Math.Clamp(0.35 + distance / 2100, 0.45, 0.95);
        int frames = Math.Max(1, (int)Math.Round(recorder.FrameRate * travelSeconds));
        for (int frame = 1; frame <= frames; frame++)
        {
            double t = (double)frame / frames;
            t = t * t * (3 - 2 * t);
            MovePointer(from + (destination - from) * t);
            Window.MouseMove(_pointer);
            await recorder.FrameAsync();
        }
    }

    private async Task ClickAsync(Control target, HeadlessVideoRecorder recorder, Point? position = null)
    {
        await PointAtAsync(target, recorder, position);
        // Resolve again after the pointer travel in case an expander changed the layout.
        Point hit = target.TranslatePoint(position ?? new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException("The click target detached while moving the pointer.");
        MovePointer(hit);
        Window.MouseMove(hit);
        Window.MouseDown(_pointer, MouseButton.Left);
        Window.MouseUp(_pointer, MouseButton.Left);
        HeadlessTestHelpers.Settle();
        await recorder.TransitionAsync(0.35);
    }

    public async Task AddClippingAsync(HeadlessVideoRecorder recorder)
    {
        FilterEffectEditor view = Window.GetVisualDescendants().OfType<FilterEffectEditor>()
            .Single(v => v.IsEffectivelyVisible && v.DataContext is FilterEffectEditorViewModel { IsGroup.Value: true });
        ToggleButton toggle = view.FindControl<ToggleButton>("expandToggle")!;
        if (toggle.IsChecked != true) await ClickAsync(toggle, recorder);
        Button add = toggle.GetVisualDescendants().OfType<Button>().Single(b => b is not ToggleButton && b.IsEffectivelyVisible);
        await ClickAsync(add, recorder);
        LibraryItemPickerFlyoutPresenter picker = Window.GetVisualDescendants().OfType<LibraryItemPickerFlyoutPresenter>().Single();
        ListBoxItem? FindClipping() => picker.GetVisualDescendants().OfType<ListBoxItem>().SingleOrDefault(i =>
            i.DataContext is PinnableLibraryItem { UserData: SingleTypeLibraryItem library } && library.ImplementationType == typeof(Clipping));
        ListBoxItem? item = FindClipping();
        if (item == null)
        {
            TextBox search = picker.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SearchTextBox");
            await ClickAsync(search, recorder);
            foreach (char character in "Clipping")
            {
                Window.KeyTextInput(character.ToString());
                await recorder.HoldAsync(0.12);
            }
            for (int i = 0; i < 30 && picker.IsBusy; i++)
            {
                await Task.Delay(50);
                HeadlessTestHelpers.Settle();
            }
            HeadlessTestHelpers.Settle();
            item = FindClipping() ?? throw new InvalidOperationException("Clipping was not found in the effect picker.");
        }
        await recorder.HoldAsync(0.8);
        await ClickAsync(item, recorder);
        await recorder.CheckpointAsync("effect-picker");
        Button accept = picker.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AcceptButton");
        await ClickAsync(accept, recorder);
        HeadlessTestHelpers.Settle();
        FilterEffectListItemEditor child = view.GetVisualDescendants().OfType<FilterEffectListItemEditor>()
            .Single(v => v.DataContext is FilterEffectEditorViewModel { Value.Value: Clipping });
        var model = (FilterEffectEditorViewModel)child.DataContext!;
        if (!model.IsExpanded.Value) await ClickAsync(child.FindControl<ToggleButton>("reorderHandle")!, recorder);
        if (!model.IsExpanded.Value) throw new InvalidOperationException("The clipping inspector did not expand.");
        await PointAtAsync(child.FindControl<ToggleButton>("reorderHandle")!, recorder);
        await recorder.HoldAsync(0.9);
    }

    public async Task AddKeyframeAsync(Guid owner, string name, HeadlessVideoRecorder recorder)
    {
        PropertyEditorMenu menu = PropertyMenu(owner, name);
        var vm = (BaseEditorViewModel)menu.DataContext!;
        Button button = menu.GetVisualDescendants().OfType<Button>().Single();
        if (!vm.HasAnimation.Value)
        {
            var flyout = (FluentAvalonia.UI.Controls.FAMenuFlyout)button.ContextFlyout!;
            flyout.Popup.ShouldUseOverlayLayer = true;
            await ClickAsync(button, recorder);
            await recorder.HoldAsync(1.1);
            var edit = flyout.Items.OfType<FluentAvalonia.UI.Controls.FAMenuFlyoutItem>()
                .Single(item => item.Text == Beutl.Language.Strings.EditAnimation);
            await PointAtAsync(edit, recorder);
            await recorder.CheckpointAsync(name + "-animation-menu");
            await ClickAsync(edit, recorder);
        }
        else
        {
            Button insert = Window.GetVisualDescendants().OfType<Button>().Single(b =>
                b.IsEffectivelyVisible && b.Classes.Contains("graph-animation")
                && b.DataContext is GraphEditorTreeItemViewModel item
                && item.Property is { } property && property.Name == name && property.GetOwnerObject()?.Id == owner);
            var item = (GraphEditorTreeItemViewModel)insert.DataContext!;
            Assert.That(item.HasKeyFrame.Value, Is.False, "The tree button must insert, not remove, a key.");
            int count = ((KeyFrameAnimation)item.Animation!).KeyFrames.Count;
            await PointAtAsync(insert, recorder);
            if (_pointer.Y > Window.ClientSize.Height - 40)
            {
                // Keep the whole recording cursor visible when the selected row is at the bottom.
                Window.MouseWheel(_pointer, new Avalonia.Vector(0, -1));
                await recorder.TransitionAsync(0.2);
            }
            bool clicked = false;
            void OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => clicked = true;
            insert.AddHandler(Button.ClickEvent, OnClick, handledEventsToo: true);
            try { await ClickAsync(insert, recorder); }
            finally { insert.RemoveHandler(Button.ClickEvent, OnClick); }
            Assert.That(clicked, Is.True,
                $"Graph tree button did not receive the click at {_pointer}; hit: {Window.InputHitTest(_pointer)?.GetType().Name}.");
            Assert.That(_pointer.Y, Is.LessThanOrEqualTo(Window.ClientSize.Height - 32));
            Assert.That(((KeyFrameAnimation)item.Animation!).KeyFrames.Count, Is.EqualTo(count + 1),
                $"CanAnimate={item.CanAnimate.Value}, selectedGraph={_graph.SelectedAnimation.Value != null}, currentItem={ReferenceEquals(insert.DataContext, item)}.");
            Assert.That(item.HasKeyFrame.Value, Is.True);
            await recorder.CheckpointAsync($"{name}-tree-key-{count + 1}");
        }
    }

    public PropertyEditorMenu PropertyMenu(Guid owner, string name) => Window.GetVisualDescendants()
        .OfType<PropertyEditorMenu>().Single(v => v.IsEffectivelyVisible
            && v.DataContext is BaseEditorViewModel vm
            && vm.PropertyAdapter.GetEngineProperty() is { } property
            && property.Name == name && property.GetOwnerObject()?.Id == owner);

    public async Task TypePropertyAsync(Guid owner, string name, string value, HeadlessVideoRecorder recorder)
    {
        PropertyEditorMenu menu = PropertyMenu(owner, name);
        PropertyEditor field = menu.GetVisualAncestors().OfType<PropertyEditor>().First();
        TextBox input = field.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_InnerTextBox");
        await ClickAsync(input, recorder);
        input.SelectAll();
        await recorder.HoldAsync(0.25);
        foreach (char character in value)
        {
            Window.KeyTextInput(character.ToString());
            await recorder.HoldAsync(0.1);
        }
        // Production's Enter behavior commits numeric fields; multiline text uses Ctrl+Enter.
        RawInputModifiers modifiers = input.AcceptsReturn ? RawInputModifiers.Control : RawInputModifiers.None;
        Window.KeyPressQwerty(PhysicalKey.Enter, modifiers);
        Window.KeyReleaseQwerty(PhysicalKey.Enter, modifiers);
        Assert.That(input.IsKeyboardFocusWithin, Is.False, "Enter must commit the edit.");
        HeadlessTestHelpers.Settle();
        await SeekAsync(_editor.Player.CurrentFrame.Value.TotalSeconds);
        await recorder.HoldAsync(0.8);
    }

    public async Task ScrubPropertyAsync(Guid owner, string name, HeadlessVideoRecorder recorder, params DemoAdjustment[] adjustments)
    {
        var field = PropertyMenu(owner, name).GetVisualAncestors().OfType<NumberEditor<float>>().First();
        var header = field.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_HeaderTextBlock");
        await PointAtAsync(header, recorder);
        await recorder.HoldAsync(0.5);
        Window.MouseDown(_pointer, MouseButton.Left);
        try
        {
            for (int index = 0; index < adjustments.Length; index++)
            {
                DemoAdjustment adjustment = adjustments[index];
                float initialValue = field.Value;
                if (Math.Abs(initialValue - adjustment.Value) < 0.001f) continue;
                // Keep the mouse held while checking the preview and reversing for a small correction.
                bool coarse = Math.Abs(adjustment.Value - initialValue) / field.SmallChange > 180;
                RawInputModifiers modifiers = coarse ? RawInputModifiers.Shift : RawInputModifiers.None;
                double coefficient = coarse ? 10 : 1;
                Point start = _pointer;
                Point end = start + new Avalonia.Vector((adjustment.Value - initialValue) / field.SmallChange / coefficient, 0);
                if (!new Rect(Window.ClientSize).Contains(end)) throw new InvalidOperationException("Property scrub leaves the captured window.");
                int frames = (int)Math.Round(adjustment.Seconds * recorder.FrameRate);
                for (int frame = 1; frame <= frames; frame++)
                {
                    double t = (double)frame / frames;
                    t = t * t * (3 - 2 * t);
                    // Whole-pixel deltas prevent float noise from appearing in the live value.
                    MovePointer(start + new Avalonia.Vector(Math.Round((end.X - start.X) * t), 0));
                    Window.MouseMove(_pointer, RawInputModifiers.LeftMouseButton | modifiers);
                    await SeekAsync(_editor.Player.CurrentFrame.Value.TotalSeconds);
                    await recorder.FrameAsync();
                }
                Assert.That(field.Value, Is.EqualTo(adjustment.Value).Within(0.001), $"{name} must change through the header drag.");
                if (index < adjustments.Length - 1) await recorder.HoldAsync(0.55);
            }
        }
        finally
        {
            Window.MouseUp(_pointer, MouseButton.Left);
        }
        HeadlessTestHelpers.Settle();
        await recorder.HoldAsync(0.8);
    }

    public async Task ScrubAsync(double seconds, HeadlessVideoRecorder recorder)
    {
        GraphEditorView? graph = Window.GetVisualDescendants().OfType<GraphEditorView>().SingleOrDefault(v => v.IsEffectivelyVisible);
        Control view = graph ?? (Control)Window.GetVisualDescendants().OfType<TimelineTabView>().Single(v => v.IsEffectivelyVisible);
        float scale = graph?.DataContext is GraphEditorViewModel model ? model.Options.Value.Scale : Timeline.Options.Value.Scale;
        ScrollViewer scroll = view.FindControl<ScrollViewer>(graph != null ? "scroll" : "ContentScroll")!;
        Border ruler = view.FindControl<Border>("RulerBar")!;
        double x = TimeSpan.FromSeconds(seconds).TimeToPixel(scale) - scroll.Offset.X;
        if (x < 0 || x > ruler.Bounds.Width - 4)
            throw new InvalidOperationException("The fixed timeline range must contain every recorded edit.");
        await PointAtAsync(ruler, recorder, new Point(Math.Clamp(x, 1, ruler.Bounds.Width - 4), 22));
        Window.MouseDown(_pointer, MouseButton.Left);
        Window.MouseUp(_pointer, MouseButton.Left);
        // Keep the composition's original sub-frame key times while the recorder samples at 30 fps.
        await SeekAsync(seconds);
        await recorder.HoldAsync(0.4);
    }

    public async Task FitGraphAsync(HeadlessVideoRecorder recorder)
    {
        // Leave the inspector crop before touching controls at the bottom of the window.
        if (CapturePointer().Focus) await OverviewAsync(recorder, 1.2);
        else _focusCamera = false;
        GraphEditorView view = Window.GetVisualDescendants().OfType<GraphEditorView>().Single(v => v.IsEffectivelyVisible);
        var model = (GraphEditorViewModel)view.DataContext!;
        Assert.That(model.Animation.KeyFrames.Count, Is.GreaterThan(1));
        await GraphActionAsync("FitAll", recorder);
        await recorder.HoldAsync(0.8);
        ScrollViewer scroll = view.FindControl<ScrollViewer>("scroll")!;
        double span = (model.Animation.KeyFrames.Last().KeyTime - model.Animation.KeyFrames.First().KeyTime)
            .TimeToPixel(model.Options.Value.Scale);
        Assert.That(span, Is.GreaterThan(scroll.Viewport.Width * 0.8), "Fit must use the horizontal plot area too.");
        foreach (Path key in view.GetVisualDescendants().OfType<Path>().Where(p => p.Name == "KeyTimeIcon"))
        {
            Point center = key.TranslatePoint(default, scroll)!.Value;
            Assert.That(center.Y, Is.InRange(16, scroll.Viewport.Height - 16),
                "Fitted keys must be visible vertically before editing the curve.");
        }
    }

    public async Task PrepareSplineHandlesAsync(HeadlessVideoRecorder recorder, bool symmetric = false)
    {
        await SelectGraphKeysAsync(recorder);
        // Expose linear tangents before shaping the curve by hand.
        await GraphActionAsync("Linear", recorder);
        if (symmetric)
        {
            GraphEditorView view = Window.GetVisualDescendants().OfType<GraphEditorView>().Single(v => v.IsEffectivelyVisible);
            Button button = view.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "TangentMode"));
            var flyout = (FluentAvalonia.UI.Controls.FAMenuFlyout)button.Flyout!;
            flyout.Popup.ShouldUseOverlayLayer = true;
            await ClickAsync(button, recorder);
            var option = flyout.Items.OfType<FluentAvalonia.UI.Controls.FARadioMenuFlyoutItem>()
                .Single(item => Equals(item.Tag, "Symmetry"));
            await PointAtAsync(option, recorder);
            await recorder.CheckpointAsync("Create-symmetry-menu");
            await recorder.HoldAsync(0.4);
            await ClickAsync(option, recorder);
            var model = (GraphEditorViewModel)view.DataContext!;
            Assert.That(model.Symmetry.Value, Is.True);
            Assert.That(model.Separately.Value, Is.False);
        }
    }

    public async Task DragSplineHandleAsync(int segmentIndex, bool incoming, float x, float y, HeadlessVideoRecorder recorder,
        bool symmetric = false)
    {
        GraphEditorView view = Window.GetVisualDescendants().OfType<GraphEditorView>().Single(v => v.IsEffectivelyVisible);
        var model = (GraphEditorViewModel)view.DataContext!;
        var item = model.SelectedView.Value!.KeyFrames[segmentIndex];
        var spline = (SplineEasing)item.Model.Easing;
        Path handle = view.GetVisualDescendants().OfType<Path>().Single(p =>
            ReferenceEquals(p.DataContext, item) && Equals(p.Tag, incoming ? "ControlPoint2" : "ControlPoint1"));
        Assert.That(handle.IsEffectivelyVisible, Is.True);
        await PointAtAsync(handle, recorder, default(Point));
        await recorder.HoldAsync(0.25);
        Point localStart = incoming ? item.ControlPoint2.Value : item.ControlPoint1.Value;
        Point localEnd = new(x * item.Width.Value, (item.Decreasing.Value ? y : 1 - y) * item.Height.Value);
        Point start = _pointer;
        Point end = start + (localEnd - localStart);
        RawInputModifiers modifiers = symmetric ? RawInputModifiers.None : RawInputModifiers.Alt;
        var opposite = symmetric ? GraphEditorTangentCoupling.OppositeSegment(item, incoming) : null;
        bool verifyCoupling = opposite != null && item.Height.Value > 0 && opposite.Height.Value > 0;
        if (symmetric) Assert.That(model.Symmetry.Value, Is.True);
        Window.MouseDown(start, MouseButton.Left, modifiers);
        Assert.That(view.ControlPointMoveState?.Segment, Is.SameAs(item));
        Assert.That(view.ControlPointMoveState?.Incoming, Is.EqualTo(incoming));
        try
        {
            int frames = (int)(1.4 * recorder.FrameRate);
            for (int i = 1; i <= frames; i++)
            {
                double t = (double)i / frames;
                t = t * t * (3 - 2 * t);
                MovePointer(start + (end - start) * t);
                Window.MouseMove(_pointer, RawInputModifiers.LeftMouseButton | modifiers);
                if (verifyCoupling)
                {
                    Point driver = GraphEditorTangentCoupling.Vector(item, incoming);
                    Point paired = GraphEditorTangentCoupling.Vector(opposite!, !incoming);
                    Assert.That(paired.X, Is.EqualTo(-driver.X).Within(0.00001), "The opposite handle must follow each drag frame.");
                    Assert.That(paired.Y, Is.EqualTo(-driver.Y).Within(0.001));
                }
                await SeekAsync(_editor.Player.CurrentFrame.Value.TotalSeconds);
                await recorder.FrameAsync();
            }
        }
        finally { Window.MouseUp(_pointer, MouseButton.Left, modifiers); }
        HeadlessTestHelpers.Settle();
        Assert.That(incoming ? spline.X2 : spline.X1, Is.EqualTo(x).Within(0.00001));
        Assert.That(incoming ? spline.Y2 : spline.Y1, Is.EqualTo(y).Within(0.00001));
        await recorder.HoldAsync(0.2);
    }

    public async Task MoveEndingKeyAsync(double localTime, HeadlessVideoRecorder recorder)
    {
        GraphEditorView view = Window.GetVisualDescendants().OfType<GraphEditorView>().Single(v => v.IsEffectivelyVisible);
        var model = (GraphEditorViewModel)view.DataContext!;
        Path key = view.GetVisualDescendants().OfType<Path>().Where(p => p.Name == "KeyTimeIcon")
            .OrderBy(p => ((GraphEditorKeyFrameViewModel)p.DataContext!).Model.KeyTime).Last();
        var item = (GraphEditorKeyFrameViewModel)key.DataContext!;
        // Move the playhead a little before the endpoint so it cannot intercept the diamond.
        await ScrubAsync(model.Element!.Start.TotalSeconds + item.Model.KeyTime.TotalSeconds - 0.08, recorder);
        await PointAtAsync(key, recorder, default(Point));
        Point start = _pointer;
        Point end = start + new Avalonia.Vector((TimeSpan.FromSeconds(localTime) - item.Model.KeyTime)
            .TimeToPixel(model.Options.Value.Scale), 0);
        Window.MouseDown(start, MouseButton.Left);
        Assert.That(view.KeyTimeMoveState?.KeyFrame, Is.SameAs(item.Model));
        try
        {
            int frames = (int)(1.3 * recorder.FrameRate);
            for (int i = 1; i <= frames; i++)
            {
                double t = (double)i / frames;
                t = t * t * (3 - 2 * t);
                MovePointer(start + (end - start) * t);
                Window.MouseMove(_pointer, RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
                await SeekAsync(_editor.Player.CurrentFrame.Value.TotalSeconds);
                await recorder.FrameAsync();
            }
        }
        finally { Window.MouseUp(_pointer, MouseButton.Left); }
        Assert.That(item.Model.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(localTime)));
        Assert.That(Convert.ToSingle(item.Model.Value), Is.EqualTo(475));
        await recorder.HoldAsync(0.5);
    }

    public async Task SelectGraphKeysAsync(HeadlessVideoRecorder recorder)
    {
        Path[] keys = Window.GetVisualDescendants().OfType<Path>()
            .Where(p => p.IsEffectivelyVisible && p.Name == "KeyTimeIcon" && p.DataContext is GraphEditorKeyFrameViewModel)
            .OrderBy(p => ((GraphEditorKeyFrameViewModel)p.DataContext!).Model.KeyTime).ToArray();
        Assert.That(keys.Length, Is.GreaterThanOrEqualTo(3));
        // The playhead overlays the ending diamond. Focus the hold key, then use the native Select All shortcut.
        await ClickAsync(keys[1], recorder, new Point(0, 0));
        RawInputModifiers command = KeyGestureHelper.GetCommandModifier() == KeyModifiers.Meta
            ? RawInputModifiers.Meta : RawInputModifiers.Control;
        Window.KeyPressQwerty(PhysicalKey.A, command);
        Window.KeyReleaseQwerty(PhysicalKey.A, command);
        HeadlessTestHelpers.Settle();
        Assert.That(keys.All(k => ((GraphEditorKeyFrameViewModel)k.DataContext!).IsSelected.Value), Is.True);
        await recorder.HoldAsync(0.2);
    }

    public async Task GraphActionAsync(string action, HeadlessVideoRecorder recorder)
    {
        GraphEditorView view = Window.GetVisualDescendants().OfType<GraphEditorView>().Single(v => v.IsEffectivelyVisible);
        Button button = view.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, action));
        if (!button.IsEnabled) throw new InvalidOperationException($"Graph action {action} is disabled.");
        await ClickAsync(button, recorder);
    }

    public async Task ShowTransformAsync(Guid owner, HeadlessVideoRecorder recorder)
    {
        TransformEditorViewModel transform = Window.GetVisualDescendants().OfType<TransformEditor>()
            .Where(v => v.IsEffectivelyVisible).Select(v => v.DataContext).OfType<TransformEditorViewModel>()
            .Single(t => t.Value.Value is Beutl.Graphics.Transformation.TransformGroup);
        await ExpandAsync(transform);
        TransformEditorViewModel translation = transform.Group.Value!.Items.Select(i => i.Context)
            .OfType<TransformEditorViewModel>().Single(t => t.Value.Value?.Id == owner);
        await ExpandAsync(translation);
        HeadlessTestHelpers.Render(2);

        Control View(TransformEditorViewModel model) => Window.GetVisualDescendants()
            .OfType<Control>().Single(v => v is TransformEditor or TransformListItemEditor && v.DataContext == model);

        async Task ExpandAsync(TransformEditorViewModel model)
        {
            if (model.IsExpanded.Value) return;
            Control view = View(model);
            ToggleButton toggle = view.FindControl<ToggleButton>(view is TransformListItemEditor ? "reorderHandle" : "expandToggle")
                ?? throw new InvalidOperationException("The transform expander is missing.");
            await ClickAsync(toggle, recorder);
            if (!model.IsExpanded.Value) throw new InvalidOperationException("Clicking the transform expander did not open it.");
        }
    }

    public async Task ShowTimelineAsync(HeadlessVideoRecorder recorder)
    {
        GraphEditorView? graph = Window.GetVisualDescendants().OfType<GraphEditorView>().SingleOrDefault(v => v.IsEffectivelyVisible);
        if (graph?.DataContext is GraphEditorViewModel model && model.Options.Value.Scale > 0.86f)
        {
            Border ruler = graph.FindControl<Border>("RulerBar")!;
            await PointAtAsync(ruler, recorder, new Point(1, 22));
            double delta = Math.Log(0.85 / model.Options.Value.Scale, 1.2) / 24;
            for (int i = 0; i < 24; i++)
            {
                Window.MouseWheel(_pointer, new Avalonia.Vector(0, delta), RawInputModifiers.Alt);
                await recorder.FrameAsync();
            }
            Window.MouseWheel(_pointer, new Avalonia.Vector(0, model.ScrollOffset.Value.X / 50));
            await recorder.TransitionAsync(0.3);
        }
        if (!Timeline.IsSelected.Value)
        {
            ToolTabStripItem tab = Window.GetVisualDescendants().OfType<ToolTabStripItem>()
                .Single(t => t.DataContext is BeutlToolDockable dock && dock.ToolContext == Timeline);
            Border title = tab.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_TabTitleHost");
            await ClickAsync(title, recorder);
        }
        HeadlessTestHelpers.Render(2);
    }

    public void ClearSelection()
    {
        _focusCamera = false;
        ((IEditorSelection)_editor.GetService(typeof(IEditorSelection))!).SelectedObject.Value = null;
        Timeline.ClearSelected();
    }

    private void MovePointer(Point point)
    {
        _pointer = point;
    }
}
