namespace Beutl.HeadlessUITests.Demos;

// Normalized source coordinates survive changes to capture size and UI layout.
internal sealed record DemoCameraTarget(double X, double Y);
internal sealed record DemoPointerFrame(double X, double Y, bool Focus, string? Checkpoint = null, string? Caption = null,
    DemoCameraTarget? CameraTarget = null, string Cursor = "Arrow", DemoCameraTarget? GraphTarget = null);
internal sealed record DemoMotionRecording(int Width, int Height, int FrameRate, List<DemoPointerFrame> Frames, double RenderScale = 1)
{
    public int LogicalWidth => (int)Math.Round(Width / RenderScale);
    public int LogicalHeight => (int)Math.Round(Height / RenderScale);
}
internal sealed record DemoCameraPose(double X, double Y, double Zoom);

internal sealed record DemoCameraSettings
{
    public double Zoom { get; init; } = 1.35;
    public double GraphZoom { get; init; } = 1;
    public double PanSeconds { get; init; } = 1.4;
    public double ZoomSeconds { get; init; } = 1.4;
    public double LookAheadSeconds { get; init; } = 0.15;
    public double DeadZone { get; init; } = 0.07;
    public double Inset { get; init; } = 0.035;
    // Outer window size in logical pixels, including the separately captured title bar.
    public int WindowWidth { get; init; } = 1470;
    public int WindowHeight { get; init; } = 956;
    public double RenderScale { get; init; } = 2;
    public string? WallpaperPath { get; init; }
    public string? TitleBarPath { get; init; }

    public static async Task<DemoCameraSettings> LoadAsync(string path)
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<DemoCameraSettings>(await File.ReadAllTextAsync(path))
            ?? throw new InvalidDataException("Camera settings are missing.");
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        return settings with
        {
            WallpaperPath = Resolve(settings.WallpaperPath),
            TitleBarPath = Resolve(settings.TitleBarPath)
        };

        string? Resolve(string? value) => value == null ? null : Path.GetFullPath(value, directory);
    }

    public void Validate()
    {
        if (!double.IsFinite(Zoom) || Zoom is < 1 or > 3
            || !double.IsFinite(GraphZoom) || GraphZoom is < 1 or > 3
            || !double.IsFinite(PanSeconds) || PanSeconds is < 0.15 or > 3
            || !double.IsFinite(ZoomSeconds) || ZoomSeconds is < 0.15 or > 3
            || !double.IsFinite(LookAheadSeconds) || LookAheadSeconds is < 0 or > 0.5
            || !double.IsFinite(DeadZone) || DeadZone is < 0 or > 0.2
            || !double.IsFinite(Inset) || Inset is < 0 or > 0.15
            || WindowWidth is < 640 or > 3840 || WindowHeight is < 480 or > 2160
            || !double.IsFinite(RenderScale) || RenderScale is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(DemoCameraSettings), "Invalid camera settings; see the documented ranges.");
    }
}

internal static class DemoCameraMotion
{
    public static DemoCameraPose[] Plan(IReadOnlyList<DemoPointerFrame> frames, int frameRate, DemoCameraSettings settings)
    {
        settings.Validate();
        if (frameRate is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(frameRate));
        if (frames.Any(f => !ValidPoint(f.X, f.Y)
            || f.CameraTarget is { } target && !ValidPoint(target.X, target.Y)
            || f.GraphTarget is { } graph && !ValidPoint(graph.X, graph.Y)))
            throw new ArgumentException("Pointer samples must lie within the normalized source frame.", nameof(frames));
        var poses = new DemoCameraPose[frames.Count];
        double x = 0.5, y = 0.5, zoom = 1, vx = 0, vy = 0, vz = 0, targetX = 0.5, targetY = 0.5;
        double dt = 1d / frameRate;
        int lookAhead = (int)Math.Round(settings.LookAheadSeconds * frameRate);
        for (int i = 0; i < frames.Count; i++)
        {
            DemoPointerFrame current = frames[i];
            bool graphFocus = current.GraphTarget != null && settings.GraphZoom > 1;
            if (current.Focus || graphFocus)
            {
                if ((graphFocus ? current.GraphTarget : current.CameraTarget) is { } target)
                {
                    // The walkthrough frames a whole operation. Moving between its controls must not pan the camera.
                    targetX = target.X;
                    targetY = target.Y;
                }
                else
                {
                    // Keep older recordings usable when they only contain cursor samples.
                    int next = i;
                    while (next < Math.Min(i + lookAhead, frames.Count - 1) && frames[next + 1].Focus
                        && frames[next + 1].CameraTarget == null) next++;
                    DemoPointerFrame pointer = frames[next];
                    targetX = Follow(targetX, pointer.X, settings.DeadZone / zoom);
                    targetY = Follow(targetY, pointer.Y, settings.DeadZone / zoom);
                }
            }
            else
            {
                targetX = targetY = 0.5;
            }
            double targetZoom = graphFocus ? settings.GraphZoom : current.Focus ? settings.Zoom : 1;
            double targetHalf = 0.5 / targetZoom;
            Step(ref x, ref vx, Math.Clamp(targetX, targetHalf, 1 - targetHalf), settings.PanSeconds, dt);
            Step(ref y, ref vy, Math.Clamp(targetY, targetHalf, 1 - targetHalf), settings.PanSeconds, dt);
            Step(ref zoom, ref vz, targetZoom, settings.ZoomSeconds, dt);
            zoom = Math.Clamp(zoom, 1, Math.Max(settings.Zoom, settings.GraphZoom));
            double half = 0.5 / zoom;
            poses[i] = new DemoCameraPose(Math.Clamp(x, half, 1 - half), Math.Clamp(y, half, 1 - half), zoom);
        }
        return poses;
    }

    private static bool ValidPoint(double x, double y)
        => double.IsFinite(x) && double.IsFinite(y) && x is >= 0 and <= 1 && y is >= 0 and <= 1;

    private static double Follow(double center, double pointer, double deadZone)
        => center + Math.CopySign(Math.Max(0, Math.Abs(pointer - center) - deadZone), pointer - center);

    // Exact critically damped spring integration: no frame-rate-dependent Euler jitter or bounce.
    private static void Step(ref double value, ref double velocity, double target, double settleSeconds, double dt)
    {
        double omega = 4.75 / settleSeconds;
        double displacement = value - target;
        double impulse = (velocity + omega * displacement) * dt;
        double decay = Math.Exp(-omega * dt);
        value = target + (displacement + impulse) * decay;
        velocity = (velocity - omega * impulse) * decay;
    }
}
