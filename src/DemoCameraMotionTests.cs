namespace Beutl.HeadlessUITests.Demos;

[TestFixture]
public sealed class DemoCameraMotionTests
{
    [Test]
    public void Fast_corner_moves_never_expose_outside_the_source_or_jump()
    {
        DemoPointerFrame[] corners = [new(0, 0, true), new(1, 1, true), new(1, 0, true), new(0, 1, true)];
        DemoPointerFrame[] frames = corners.SelectMany(c => Enumerable.Repeat(c, 60))
            .Concat(Enumerable.Repeat(new DemoPointerFrame(1, 0, false), 150)).ToArray();
        var settings = new DemoCameraSettings();
        DemoCameraPose[] poses = DemoCameraMotion.Plan(frames, 30, settings);
        foreach (DemoCameraPose pose in poses)
        {
            double half = 0.5 / pose.Zoom;
            Assert.That(pose.Zoom, Is.InRange(1, settings.Zoom));
            Assert.That(pose.X - half, Is.GreaterThanOrEqualTo(-1e-12));
            Assert.That(pose.Y - half, Is.GreaterThanOrEqualTo(-1e-12));
            Assert.That(pose.X + half, Is.LessThanOrEqualTo(1 + 1e-12));
            Assert.That(pose.Y + half, Is.LessThanOrEqualTo(1 + 1e-12));
        }
        for (int i = 1; i < poses.Length; i++)
        {
            Assert.That(Math.Abs(poses[i].X - poses[i - 1].X), Is.LessThan(0.05));
            Assert.That(Math.Abs(poses[i].Y - poses[i - 1].Y), Is.LessThan(0.05));
            Assert.That(Math.Abs(poses[i].Zoom - poses[i - 1].Zoom), Is.LessThan(0.06));
        }
        Assert.That(poses[^1].Zoom, Is.EqualTo(1).Within(1e-5));
        Assert.That(poses[^1].X, Is.EqualTo(0.5).Within(1e-5));
        Assert.That(poses[^1].Y, Is.EqualTo(0.5).Within(1e-5));
    }

    [Test]
    public void Tiny_pointer_moves_do_not_make_the_camera_drift()
    {
        DemoPointerFrame[] frames = Enumerable.Range(0, 180)
            .Select(i => new DemoPointerFrame(0.5 + Math.Sin(i) * 0.01, 0.5 + Math.Cos(i) * 0.01, true)).ToArray();
        foreach (DemoCameraPose pose in DemoCameraMotion.Plan(frames, 30, new DemoCameraSettings()))
        {
            Assert.That(pose.X, Is.EqualTo(0.5));
            Assert.That(pose.Y, Is.EqualTo(0.5));
        }
    }

    [Test]
    public void Overview_does_not_chase_the_parked_cursor()
    {
        DemoPointerFrame[] frames = Enumerable.Repeat(new DemoPointerFrame(1, 0, false), 60).ToArray();
        Assert.That(DemoCameraMotion.Plan(frames, 30, new DemoCameraSettings()),
            Is.All.EqualTo(new DemoCameraPose(0.5, 0.5, 1)));
    }

    [Test]
    public void Operation_framing_stays_fixed_while_the_pointer_crosses_panels()
    {
        var target = new DemoCameraTarget(0.75, 0.65);
        DemoPointerFrame[] moving = Enumerable.Range(0, 180).Select(i =>
            new DemoPointerFrame(i % 2, (i / 2) % 2, true, CameraTarget: target)).ToArray();
        DemoPointerFrame[] parked = Enumerable.Repeat(new DemoPointerFrame(0.5, 0.5, true, CameraTarget: target), 180).ToArray();
        var settings = new DemoCameraSettings();
        Assert.That(DemoCameraMotion.Plan(moving, 30, settings), Is.EqualTo(DemoCameraMotion.Plan(parked, 30, settings)),
            "An operation's framing must not follow each trip from the ruler to the inspector.");
    }

    [Test]
    public void Camera_timing_is_consistent_at_thirty_and_sixty_fps()
    {
        var focus = new DemoPointerFrame(0.8, 0.2, true);
        var settings = new DemoCameraSettings { DeadZone = 0, LookAheadSeconds = 0 };
        DemoCameraPose[] thirty = DemoCameraMotion.Plan(Enumerable.Repeat(focus, 30).ToArray(), 30, settings);
        DemoCameraPose[] sixty = DemoCameraMotion.Plan(Enumerable.Repeat(focus, 60).ToArray(), 60, settings);
        for (int i = 0; i < 30; i++)
        {
            Assert.That(thirty[i].X, Is.EqualTo(sixty[2 * i + 1].X).Within(1e-12));
            Assert.That(thirty[i].Y, Is.EqualTo(sixty[2 * i + 1].Y).Within(1e-12));
            Assert.That(thirty[i].Zoom, Is.EqualTo(sixty[2 * i + 1].Zoom).Within(1e-12));
        }
    }
}
