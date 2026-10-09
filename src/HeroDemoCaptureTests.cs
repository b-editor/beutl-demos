using System.Text.Json.Nodes;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Beutl.Controls.Styling.Themes;
using Beutl.ViewModels;
using FluentAvalonia.Styling;

namespace Beutl.HeadlessUITests.Demos;

[TestFixture]
[SetCulture("en-US")]
[SetUICulture("en-US")]
[Explicit("Records the real editor UI through Headless and MCP; requires a GPU and FFmpeg.")]
public sealed class HeroDemoCaptureTests
{
    private static void RegisterDemoFont()
    {
        using Stream font = typeof(HeroDemoCaptureTests).Assembly.GetManifestResourceStream("DemoHeroFont.ttf")
            ?? throw new InvalidOperationException("The bundled Inter typeface is missing.");
        Beutl.Media.FontManager.Instance.AddFont(font);
    }

    [AvaloniaTest]
    public async Task Record_hero_editorial_workflow()
    {
        Assert.That(Environment.GetEnvironmentVariable("BEUTL_DEMO_CULTURE"), Is.EqualTo("en-US"),
            "Set BEUTL_DEMO_CULTURE=en-US before starting the recorder so the UI and library initialize in English.");
        GpuTestGate.EnsureAvailable();
        await TestReset.ResetShellAsync();
        RegisterDemoFont();
        string parent = Environment.GetEnvironmentVariable("BEUTL_DEMO_OUTPUT_DIR")
            ?? System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "demo-captures");
        string output = System.IO.Path.GetFullPath(System.IO.Path.Combine(parent,
            $"hero-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}"));
        Directory.CreateDirectory(output);
        TestContext.Progress.WriteLine($"Demo output: {output}");
        string? settingsPath = Environment.GetEnvironmentVariable("BEUTL_DEMO_CAMERA_SETTINGS");
        var camera = settingsPath == null ? new DemoCameraSettings() : await DemoCameraSettings.LoadAsync(settingsPath);
        camera.Validate();
        bool stillsOnly = Environment.GetEnvironmentVariable("BEUTL_DEMO_STILLS_ONLY") == "1";

        using Stream resource = typeof(HeroDemoCaptureTests).Assembly.GetManifestResourceStream(
            "Beutl.HeadlessUITests.Assets.Demos.hero-editorial.json")!;
        var templates = JsonNode.Parse(resource)!["Elements"]!.AsArray()
            .Select(n => n!.AsObject()).ToDictionary(e => e["Name"]!.GetValue<string>());

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        Application.Current.Styles.OfType<FluentAvaloniaTheme>().Single().CustomAccentColor = BeutlDarkBorderTheme.AccentColor;
        await using DemoMcpClient mcp = await DemoMcpClient.StartAsync(output);
        string fontFamily = templates["Create. / Mask reveal"]["Objects"]![0]!["FontFamily"]!.GetValue<string>();
        JsonNode fonts = await mcp.CallAsync("list_fonts", new() { ["nameFilter"] = fontFamily });
        Assert.That(fonts["families"]!.AsArray().Any(f => f!["name"]!.GetValue<string>() == fontFamily
            && f["typefaces"]!.AsArray().Any(t => t!["weight"]!.GetValue<int>() == 900 && t["style"]!.GetValue<string>() == "Normal")),
            Is.True, $"The bundled {fontFamily} Black (weight 900) must be registered before recording. Runtime font catalog: {fonts}");
        await mcp.CallAsync("create_project", new()
        {
            ["path"] = "Hero Editorial Wipe.bep",
            ["width"] = 1920,
            ["height"] = 1080,
            ["frameRate"] = 100,
            ["duration"] = "00:00:09"
        });
        JsonNode attached = await mcp.CallAsync("attach_active_editor");
        Assert.That(attached["source"]!.GetValue<string>(), Is.EqualTo("LiveEditor"));
        // The LP walkthrough explicitly starts from a prepared layout, not a new-project tutorial.
        await mcp.EditAsync(DemoWorkflow.StartingLayout(templates));
        int titleBarHeight = 0;
        if (camera.TitleBarPath != null)
        {
            string titleBarPath = System.IO.Path.Combine(output, "macos-titlebar.png");
            titleBarHeight = DemoMacTitleBar.Capture(titleBarPath, camera.WindowWidth, camera.RenderScale);
            camera = camera with { TitleBarPath = titleBarPath };
        }
        if (camera.WallpaperPath is { } wallpaper)
        {
            string wallpaperPath = System.IO.Path.Combine(output, "wallpaper.png");
            File.Copy(wallpaper, wallpaperPath);
            camera = camera with { WallpaperPath = wallpaperPath };
        }
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        foreach (var element in editor.Scene.Children)
            Assert.That(element.AccentColor, Is.EqualTo(Beutl.Editor.Components.Helpers.ColorGenerator.GenerateColor(
                element.Objects[0].GetType().FullName!)));
        bool pointerLock = Beutl.Configuration.GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty;
        // Native pointer lock reads the physical mouse, which must never be touched by a headless capture.
        Beutl.Configuration.GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty = false;
        int contentHeight = camera.WindowHeight - titleBarHeight;
        Assert.That(contentHeight, Is.GreaterThanOrEqualTo(480), "The title bar must leave room for the editor.");
        var ui = new DemoEditor(editor, camera.WindowWidth, contentHeight, camera.RenderScale);
        TestContext.Progress.WriteLine($"Window: {camera.WindowWidth}x{camera.WindowHeight} logical pixels including a {titleBarHeight}px title bar, {camera.RenderScale}x Retina scale.");
        try
        {
            await ui.SeekAsync(1.2);
            string originalVideo = System.IO.Path.Combine(output, "editor-demo.mp4");
            await using var video = new HeadlessVideoRecorder(ui.Window, originalVideo, 30, ui.CapturePointer, encodeVideo: !stillsOnly);
            await video.HoldAsync(0.3);

            await new DemoWorkflow(ui, video, output).BuildAsync(templates);

            Assert.That(editor.Scene.Children.Count, Is.EqualTo(templates.Count));
            Assert.That(await editor.SaveAsync(), Is.True);
            await video.CompleteAsync();
            TestContext.Progress.WriteLine($"Captured {video.FrameCount} frames at {video.FrameRate} fps, {camera.RenderScale}x UI scale: {output}");
            if (!stillsOnly) await DemoReferenceVerifier.VerifyAsync(editor.Scene, templates.Values, output);
            await File.WriteAllTextAsync(System.IO.Path.Combine(output, "camera-settings.json"),
                JsonSerializer.Serialize(camera with
                {
                    WallpaperPath = System.IO.Path.GetFileName(camera.WallpaperPath),
                    TitleBarPath = System.IO.Path.GetFileName(camera.TitleBarPath)
                }, new JsonSerializerOptions { WriteIndented = true }));
            Assert.That(video.FrameCount / (double)video.FrameRate, Is.InRange(60, 120), "Keep the LP walkthrough between one and two minutes.");
            if (stillsOnly)
            {
                await DemoCameraRenderer.RenderPreviewsAsync(originalVideo, output, camera);
                Assert.That(Directory.GetFiles(output, "*.mp4", SearchOption.AllDirectories), Is.Empty,
                    "A still-preview run must never start a video encoder.");
            }
            else if (Environment.GetEnvironmentVariable("BEUTL_DEMO_CAPTURE_ONLY") != "1")
            {
                TestContext.Progress.WriteLine("Rendering: camera with stable operation framing");
                await DemoCameraRenderer.RenderAsync(originalVideo, System.IO.Path.Combine(output, "editor-demo-camera.mp4"), camera);
            }
        }
        catch
        {
            using var failure = ui.Window.CaptureRenderedFrame();
            failure?.Save(System.IO.Path.Combine(output, "failed-step.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            throw;
        }
        finally
        {
            Beutl.Configuration.GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty = pointerLock;
            ui.Window.Close();
            await TestReset.ResetShellAsync();
        }
    }

    [Test]
    public async Task Render_camera_from_recording()
    {
        string sourceDirectory = Environment.GetEnvironmentVariable("BEUTL_DEMO_SOURCE_DIR")
            ?? throw new InvalidOperationException("Set BEUTL_DEMO_SOURCE_DIR to a capture directory containing editor-demo.mp4 and its .motion.json.");
        sourceDirectory = System.IO.Path.GetFullPath(sourceDirectory);
        string settingsPath = Environment.GetEnvironmentVariable("BEUTL_DEMO_CAMERA_SETTINGS")
            ?? System.IO.Path.Combine(sourceDirectory, "camera-settings.json");
        var settings = await DemoCameraSettings.LoadAsync(settingsPath);
        // Reuse the title row captured at this recording's real window width and DPI.
        string titleBar = System.IO.Path.Combine(sourceDirectory, "macos-titlebar.png");
        if (File.Exists(titleBar)) settings = settings with { TitleBarPath = titleBar };
        string output = System.IO.Path.Combine(sourceDirectory, $"editor-demo-camera-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.mp4");
        await DemoCameraRenderer.RenderAsync(System.IO.Path.Combine(sourceDirectory, "editor-demo.mp4"), output, settings);
        TestContext.Progress.WriteLine($"Camera video: {output}");
    }

    [AvaloniaTest]
    public async Task Preview_macos_window_appearance()
    {
        RegisterDemoFont();
        Assert.That(Environment.GetEnvironmentVariable("BEUTL_DEMO_CULTURE"), Is.EqualTo("en-US"));
        string source = System.IO.Path.GetFullPath(Environment.GetEnvironmentVariable("BEUTL_DEMO_SOURCE_DIR")
            ?? throw new InvalidOperationException("Set BEUTL_DEMO_SOURCE_DIR to the accepted recording directory."));
        string wallpaper = Environment.GetEnvironmentVariable("BEUTL_DEMO_WALLPAPER")
            ?? throw new InvalidOperationException("Set BEUTL_DEMO_WALLPAPER to the wallpaper image.");
        string output = System.IO.Path.Combine(source, $"appearance-preview-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
        Directory.CreateDirectory(output);
        File.Copy(wallpaper, System.IO.Path.Combine(output, "wallpaper.png"));
        TestContext.Progress.WriteLine($"Appearance output: {output}");
        await TestReset.ResetShellAsync();
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        Application.Current.Styles.OfType<FluentAvaloniaTheme>().Single().CustomAccentColor = BeutlDarkBorderTheme.AccentColor;
        await using DemoMcpClient mcp = await DemoMcpClient.StartAsync(source);
        try
        {
            await mcp.CallAsync("open_project", new() { ["path"] = "Hero Editorial Wipe.bep" });
            await mcp.CallAsync("attach_active_editor");
            var recording = JsonSerializer.Deserialize<DemoMotionRecording>(await File.ReadAllTextAsync(
                System.IO.Path.Combine(source, "editor-demo.motion.json")))!;
            DemoMacTitleBar.Capture(System.IO.Path.Combine(output, "macos-titlebar.png"), recording.LogicalWidth, recording.RenderScale);
            var settings = (await DemoCameraSettings.LoadAsync(System.IO.Path.Combine(source, "camera-settings.json"))) with
            {
                Inset = 0.055, RenderScale = recording.RenderScale, WallpaperPath = "wallpaper.png", TitleBarPath = "macos-titlebar.png"
            };
            string settingsPath = System.IO.Path.Combine(output, "camera-settings.json");
            await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            await DemoCameraRenderer.RenderPreviewsAsync(System.IO.Path.Combine(source, "editor-demo.mp4"),
                output, await DemoCameraSettings.LoadAsync(settingsPath));
            Assert.That(Directory.GetFiles(output, "*.mp4"), Is.Empty, "Appearance review must not export a video.");
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task Verify_manual_spline_equivalence()
    {
        RegisterDemoFont();
        GpuTestGate.EnsureAvailable();
        await TestReset.ResetShellAsync();
        string parent = Environment.GetEnvironmentVariable("BEUTL_DEMO_OUTPUT_DIR")
            ?? TestContext.CurrentContext.WorkDirectory;
        string output = System.IO.Path.GetFullPath(System.IO.Path.Combine(parent,
            $"spline-reference-{DateTime.UtcNow:yyyyMMdd-HHmmss}"));
        Directory.CreateDirectory(output);
        TestContext.Progress.WriteLine($"Spline reference output: {output}");
        using Stream resource = typeof(HeroDemoCaptureTests).Assembly.GetManifestResourceStream(
            "Beutl.HeadlessUITests.Assets.Demos.hero-editorial.json")!;
        var templates = JsonNode.Parse(resource)!["Elements"]!.AsArray().Select(n => n!.AsObject()).ToArray();
        var elements = new JsonArray(templates.Select(n => n.DeepClone()).ToArray());
        JsonObject Spline(float y) => new() { ["X1"] = 1f / 3, ["Y1"] = y, ["X2"] = 2f / 3, ["Y2"] = y };
        JsonNode Root(string name) => elements.Single(n => n!["Name"]!.GetValue<string>() == name)!["Objects"]![0]!;
        var reveal = Root("Create. / Mask reveal")["FilterEffect"]!["Animations"]!["Right"]!["KeyFrames"]!.AsArray();
        JsonNode middle = reveal[2]!.DeepClone();
        middle["Value"] = 718;
        middle["KeyTime"] = "00:00:00.4900000";
        middle["Easing"] = Spline(0);
        reveal.Insert(2, middle);
        reveal[3]!["Easing"] = Spline(1);
        Root("Animate. / Zoom")["Transform"]!["Children"]![0]!["Animations"]!["Scale"]!["KeyFrames"]![2]!["Easing"] = Spline(0);
        await using DemoMcpClient mcp = await DemoMcpClient.StartAsync(output);
        try
        {
            await mcp.CallAsync("create_project", new()
            {
                ["path"] = "Spline comparison.bep", ["width"] = 1920, ["height"] = 1080,
                ["frameRate"] = 100, ["duration"] = "00:00:09"
            });
            await mcp.CallAsync("attach_active_editor");
            await mcp.EditAsync(new JsonObject { ["Elements"] = elements });
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            await DemoReferenceVerifier.VerifyAsync(editor.Scene, templates, output);
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task Verify_saved_hero_composition()
    {
        RegisterDemoFont();
        GpuTestGate.EnsureAvailable();
        await TestReset.ResetShellAsync();
        string source = Environment.GetEnvironmentVariable("BEUTL_DEMO_SOURCE_DIR")
            ?? throw new InvalidOperationException("Set BEUTL_DEMO_SOURCE_DIR to the saved capture directory.");
        using Stream resource = typeof(HeroDemoCaptureTests).Assembly.GetManifestResourceStream(
            "Beutl.HeadlessUITests.Assets.Demos.hero-editorial.json")!;
        var templates = JsonNode.Parse(resource)!["Elements"]!.AsArray().Select(n => n!.AsObject()).ToArray();
        await using DemoMcpClient mcp = await DemoMcpClient.StartAsync(source);
        try
        {
            await mcp.CallAsync("open_project", new() { ["path"] = "Hero Editorial Wipe.bep" });
            await mcp.CallAsync("attach_active_editor");
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            string output = System.IO.Path.Combine(source, $"verification-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(output);
            await DemoReferenceVerifier.VerifyAsync(editor.Scene, templates, output);
        }
        finally { await TestReset.ResetShellAsync(); }
    }
}
