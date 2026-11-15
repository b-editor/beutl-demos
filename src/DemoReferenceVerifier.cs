using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.HeadlessUITests.Demos;

internal static class DemoReferenceVerifier
{
    public static async Task VerifyAsync(Scene edited, IEnumerable<JsonObject> templates, string output)
    {
        var reference = new Scene(1920, 1080, "Original MCP composition") { Duration = { Value = MediaTime.FromSeconds(9) } };
        foreach (JsonObject template in templates)
            reference.Children.Add((Element)CoreSerializer.DeserializeFromJsonObject(template, typeof(Element)));
        var project = new Project();
        project.FrameRate.Value = new Rational(30);
        project.Items.Add(reference);
        // Animation owners resolve local clocks when attached to an application hierarchy,
        // exactly as the production MCP renderer attaches its isolated scene snapshots.
        _ = new BeutlApplication { Project = { Value = project } };
        // Keep the original animation times and easings as the oracle, including subframe keys.
        await using var video = new DemoVideoEncoder(Path.Combine(output, "hero-final.mp4"), 1920, 1080, 30, "bgra");
        using var expectedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var actualHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        // Render the reference in a pass of its own, then the result. In Beutl 2.0.0-preview.13, two renderers drawing the
        // same frame in turn can differ in one or two antialiased pixels, even for two copies of one composition.
        var frames = new byte[270][];
        SceneRenderer expected = await RenderThread.Dispatcher.InvokeAsync(() => ExportRendererFactory.Create(reference));
        try
        {
            for (int frame = 0; frame < 270; frame++)
            {
                int current = frame;
                frames[current] = await RenderThread.Dispatcher.InvokeAsync(() =>
                {
                    expected.Render(expected.Compositor.EvaluateGraphics(MediaTime.FromFrames(current, new Rational(30))));
                    using Bitmap raw = expected.Snapshot();
                    using Bitmap a = raw.Convert(BitmapColorType.Bgra8888, colorSpace: BitmapColorSpace.Srgb);
                    expectedHash.AppendData(a.GetPixelSpan());
                    if (current is 119 or 133 or 149)
                        a.Save(Path.Combine(output, $"reference-{current:D3}.png"), EncodedImageFormat.Png);
                    return SHA256.HashData(a.GetPixelSpan());
                });
            }
        }
        finally
        {
            await RenderThread.Dispatcher.InvokeAsync(() => expected.Dispose());
        }
        SceneRenderer actual = await RenderThread.Dispatcher.InvokeAsync(() => ExportRendererFactory.Create(edited));
        int? mismatch = null;
        try
        {
            for (int frame = 0; frame < 270; frame++)
            {
                int current = frame;
                byte[] pixels = await RenderThread.Dispatcher.InvokeAsync(() =>
                {
                    actual.Render(actual.Compositor.EvaluateGraphics(MediaTime.FromFrames(current, new Rational(30))));
                    using Bitmap raw = actual.Snapshot();
                    using Bitmap b = raw.Convert(BitmapColorType.Bgra8888, colorSpace: BitmapColorSpace.Srgb);
                    actualHash.AppendData(b.GetPixelSpan());
                    bool equal = SHA256.HashData(b.GetPixelSpan()).AsSpan().SequenceEqual(frames[current]);
                    if (!equal || current is 119 or 133 or 149)
                        b.Save(Path.Combine(output, $"result-{current:D3}.png"), EncodedImageFormat.Png);
                    if (!equal) mismatch ??= current;
                    return b.GetPixelSpan().ToArray();
                });
                if (mismatch != null) break;
                await video.WriteAsync(pixels);
            }
        }
        finally
        {
            await RenderThread.Dispatcher.InvokeAsync(() => actual.Dispose());
        }
        if (mismatch is int failed)
        {
            // Draw the reference's frame again for comparison, now that the result's renderer is gone.
            await RenderThread.Dispatcher.InvokeAsync(() =>
            {
                using SceneRenderer again = ExportRendererFactory.Create(reference);
                again.Render(again.Compositor.EvaluateGraphics(MediaTime.FromFrames(failed, new Rational(30))));
                using Bitmap raw = again.Snapshot();
                using Bitmap a = raw.Convert(BitmapColorType.Bgra8888, colorSpace: BitmapColorSpace.Srgb);
                a.Save(Path.Combine(output, $"reference-{failed:D3}.png"), EncodedImageFormat.Png);
            });
            Assert.Fail($"Frame {failed} at {MediaTime.FromFrames(failed, new Rational(30))} must exactly match the original MCP composition.");
        }
        await video.CompleteAsync();
        string expectedDigest = Convert.ToHexString(expectedHash.GetHashAndReset());
        string actualDigest = Convert.ToHexString(actualHash.GetHashAndReset());
        Assert.That(actualDigest, Is.EqualTo(expectedDigest));
        await File.WriteAllTextAsync(Path.Combine(output, "composition-verification.json"), JsonSerializer.Serialize(new
        {
            FrameCount = 270, FrameRate = 30, Width = 1920, Height = 1080,
            ReferenceSha256 = expectedDigest, ResultSha256 = actualDigest
        }, new JsonSerializerOptions { WriteIndented = true }));
        TestContext.Progress.WriteLine("Verified: all 270 full-resolution frames exactly match the original MCP composition.");
    }
}
