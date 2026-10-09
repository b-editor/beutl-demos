using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Beutl.HeadlessUITests.Demos;

// Follow the same captured-element precedence as Avalonia's native cursor selection.
// Reading the inherited Cursor property also picks up style changes during a drag.
internal sealed class DemoCursorState
{
    private readonly Window _window;
    private IPointer? _pointer;

    public DemoCursorState(Window window)
    {
        _window = window;
        window.AddHandler(InputElement.PointerMovedEvent, Track, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerPressedEvent, Track, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerReleasedEvent, Track, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void Track(object? sender, PointerEventArgs e) => _pointer = e.Pointer;

    public string At(Point position)
        => (_pointer?.Captured ?? _window.InputHitTest(position)) is InputElement element
            ? element.Cursor?.ToString() ?? "Arrow"
            : "Arrow";
}
