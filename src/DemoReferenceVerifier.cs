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
        var reference = new Scene(1920, 1080, "Original MCP composition") { Duration = TimeSpan.FromSeconds(9) };
        foreach (JsonObject template in templates)
            reference.Children.Add((Element)CoreSerializer.DeserializeFromJsonObject(template, typeof(Element)));
        var project = new Project();
        project.Variables[ProjectVariableKeys.FrameRate] = "30";
        project.Items.Add(reference);
        // Animation owners resolve local clocks when attached to an application hierarchy,
        // exactly as the production MCP renderer attaches its isolated scene snapshots.
        _ = new BeutlApplication { Project = project };
        // Keep the original animation times and easings as the oracle, including subframe keys.
        await using var video = new DemoVideoEncoder(Path.Combine(output, "hero-final.mp4"), 1920, 1080, 30, "bgra");
        using var expectedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var actualHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        SceneRenderer expected = await RenderThread.Dispatcher.InvokeAsync(() => ExportRendererFactory.Create(reference));
        SceneRenderer actual = await RenderThread.Dispatcher.InvokeAsync(() => ExportRendererFactory.Create(edited));
        try
        {
            for (int frame = 0; frame < 270; frame++)
            {
                int current = frame;
                byte[] pixels = await RenderThread.Dispatcher.InvokeAsync(() =>
                {
                    TimeSpan time = TimeSpan.FromSeconds(current / 30d);
                    expected.Render(expected.Compositor.EvaluateGraphics(time));
                    actual.Render(actual.Compositor.EvaluateGraphics(time));
                    using Bitmap rawA = expected.Snapshot();
                    using Bitmap rawB = actual.Snapshot();
                    using Bitmap a = rawA.Convert(BitmapColorType.Bgra8888, colorSpace: BitmapColorSpace.Srgb);
                    using Bitmap b = rawB.Convert(BitmapColorType.Bgra8888, colorSpace: BitmapColorSpace.Srgb);
                    expectedHash.AppendData(a.GetPixelSpan());
                    actualHash.AppendData(b.GetPixelSpan());
                    bool equal = a.GetPixelSpan().SequenceEqual(b.GetPixelSpan());
                    if (!equal || current is 119 or 133 or 149)
                    {
                        a.Save(Path.Combine(output, $"reference-{current:D3}.png"), EncodedImageFormat.Png);
                        b.Save(Path.Combine(output, $"result-{current:D3}.png"), EncodedImageFormat.Png);
                    }
                    Assert.That(equal, Is.True, $"Frame {current} at {time} must exactly match the original MCP composition.");
                    return b.GetPixelSpan().ToArray();
                });
                await video.WriteAsync(pixels);
            }
        }
        finally
        {
            await RenderThread.Dispatcher.InvokeAsync(() => { expected.Dispose(); actual.Dispose(); });
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
