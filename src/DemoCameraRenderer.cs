using System.Diagnostics;
using System.Text.Json;
using SkiaSharp;

namespace Beutl.HeadlessUITests.Demos;

internal static class DemoCameraRenderer
{
    // The camera pass never touches Avalonia controls; keep decoding/compositing off the UI dispatcher.
    public static Task RenderAsync(string sourcePath, string outputPath, DemoCameraSettings settings)
        => Task.Run(() => RenderCoreAsync(sourcePath, outputPath, settings));

    public static async Task RenderPreviewsAsync(string sourcePath, string outputDirectory, DemoCameraSettings settings)
    {
        var recording = JsonSerializer.Deserialize<DemoMotionRecording>(
            await File.ReadAllTextAsync(Path.ChangeExtension(sourcePath, ".motion.json")))
            ?? throw new InvalidDataException("Missing pointer recording.");
        using var composer = new DemoFrameComposer(recording.OutputWidth, recording.OutputHeight, settings);
        DemoCameraPose[] poses = DemoCameraMotion.Plan(composer.DesktopFrames(recording.Frames), recording.FrameRate, settings);
        using var output = new SKBitmap(new SKImageInfo(recording.OutputWidth, recording.OutputHeight, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(output);
        string[] checkpoints = ["04-finished", "Create-spline-handles", "Animate-spline-handles", "effect-picker"];
        for (int i = 0; i < checkpoints.Length; i++)
        {
            string checkpoint = checkpoints[i];
            int frame = recording.Frames.FindIndex(f => f.Checkpoint == checkpoint);
            if (frame < 0) throw new InvalidDataException($"Missing preview checkpoint: {checkpoint}");
            using SKBitmap source = SKBitmap.Decode(Path.Combine(Path.GetDirectoryName(sourcePath)!, checkpoint + ".png"))
                ?? throw new InvalidDataException($"Missing checkpoint image: {checkpoint}");
            Assert.That(source.Width, Is.EqualTo(recording.Width));
            Assert.That(source.Height, Is.EqualTo(recording.Height));
            composer.Draw(canvas, source, poses[frame]);
            using SKData png = output.Encode(SKEncodedImageFormat.Png, 100);
            string path = Path.Combine(outputDirectory, $"preview-{i + 1:00}-{checkpoint}.png");
            await File.WriteAllBytesAsync(path, png.ToArray());
            TestContext.Progress.WriteLine($"Appearance preview: {path}");
        }
    }

    private static async Task RenderCoreAsync(string sourcePath, string outputPath, DemoCameraSettings settings)
    {
        settings.Validate();
        DemoMotionRecording recording = JsonSerializer.Deserialize<DemoMotionRecording>(
            await File.ReadAllTextAsync(Path.ChangeExtension(sourcePath, ".motion.json")))
            ?? throw new InvalidDataException("Missing pointer recording.");
        if (recording.Width <= 0 || recording.Height <= 0 || recording.Width % 2 != 0 || recording.Height % 2 != 0
            || recording.Frames.Count == 0)
            throw new InvalidDataException("Invalid recording dimensions or empty frame timeline.");
        using var composer = new DemoFrameComposer(recording.OutputWidth, recording.OutputHeight, settings);
        DemoCameraPose[] poses = DemoCameraMotion.Plan(composer.DesktopFrames(recording.Frames), recording.FrameRate, settings);
        var start = new ProcessStartInfo(DemoVideoEncoder.Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string arg in new[]
        {
            "-hide_banner", "-loglevel", "error", "-i", Path.GetFullPath(sourcePath),
            "-map", "0:v:0", "-an", "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1"
        }) start.ArgumentList.Add(arg);
        using Process decoder = Process.Start(start) ?? throw new InvalidOperationException("Could not start the video decoder.");
        Task<string> errors = decoder.StandardError.ReadToEndAsync();
        try
        {
            await using var encoder = new DemoVideoEncoder(outputPath, recording.OutputWidth, recording.OutputHeight, recording.FrameRate, "bgra");
            var info = new SKImageInfo(recording.Width, recording.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using var source = new SKBitmap(info);
            using var output = new SKBitmap(new SKImageInfo(recording.OutputWidth, recording.OutputHeight, SKColorType.Bgra8888, SKAlphaType.Opaque));
            using var canvas = new SKCanvas(output);
            var pixels = new byte[checked(recording.Width * recording.Height * 4)];
            var outputPixels = new byte[checked(recording.OutputWidth * recording.OutputHeight * 4)];
            for (int frame = 0; frame < poses.Length; frame++)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await decoder.StandardOutput.BaseStream.ReadExactlyAsync(pixels, timeout.Token);
                pixels.AsSpan().CopyTo(source.GetPixelSpan());
                composer.Draw(canvas, source, poses[frame]);
                output.GetPixelSpan().CopyTo(outputPixels);
                await encoder.WriteAsync(outputPixels);
                if (recording.Frames[frame].Checkpoint is { } checkpoint)
                {
                    string name = Path.GetFileNameWithoutExtension(outputPath) + "-" + Path.GetFileName(checkpoint) + ".png";
                    using SKData png = output.Encode(SKEncodedImageFormat.Png, 100);
                    await File.WriteAllBytesAsync(Path.Combine(Path.GetDirectoryName(outputPath)!, name), png.ToArray());
                }
            }
            using var completionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (await decoder.StandardOutput.BaseStream.ReadAsync(pixels.AsMemory(0, 1), completionTimeout.Token) != 0)
                throw new InvalidDataException("Video has more frames than the pointer recording; refusing to desynchronize the camera.");
            await decoder.WaitForExitAsync(completionTimeout.Token);
            if (decoder.ExitCode != 0) throw new InvalidOperationException($"Video decoding failed: {await errors}");
            await encoder.CompleteAsync();
        }
        finally
        {
            if (!decoder.HasExited) decoder.Kill(entireProcessTree: true);
            await decoder.WaitForExitAsync();
        }
    }
}
