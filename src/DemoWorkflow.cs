using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;

namespace Beutl.HeadlessUITests.Demos;

// A prepared layout supplies the supporting artwork. Every recorded refinement uses real UI input.
internal sealed class DemoWorkflow(DemoEditor ui, HeadlessVideoRecorder video, string output)
{
    private readonly List<object> _steps = [];
    private const string Create = "Create. / Mask reveal";
    private const string Animate = "Animate. / Zoom";
    private const string Yours = "Make it yours. / yours.";

    public static JsonObject StartingLayout(IReadOnlyDictionary<string, JsonObject> templates)
    {
        var elements = new JsonArray(templates.Values.Select(e => e.DeepClone()).ToArray());
        JsonObject Object(string name) => elements.Single(e => e!["Name"]!.GetValue<string>() == name)!["Objects"]![0]!.AsObject();
        foreach (JsonNode? element in elements)
        {
            string type = element!["Objects"]![0]!["$type"]!.GetValue<string>();
            // Match ElementAdderImpl's native accent-color generation by engine-object type.
            string typeName = type[(type.IndexOf(']') + 1)..].Replace(':', '.');
            element["AccentColor"] = ColorGenerator.GenerateColor(typeName).ToString();
        }
        JsonObject create = Object(Create);
        create["Text"] = "Your text";
        create["FilterEffect"] = new JsonObject
        {
            ["$type"] = "[Beutl.Engine]Beutl.Graphics.Effects:FilterEffectGroup",
            ["Children"] = new JsonArray()
        };
        Object(Animate)["Transform"]!["Children"]![0]!.AsObject().Remove("Animations");
        // A separate layout offset lets the designer align the card without disturbing its motion.
        Object(Yours)["Transform"]!["Children"]!.AsArray().Add(new JsonObject
        {
            ["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:TranslateTransform",
            ["Name"] = "Card alignment", ["X"] = 0, ["Y"] = 75
        });
        return new JsonObject { ["Elements"] = elements };
    }

    public async Task BuildAsync(IReadOnlyDictionary<string, JsonObject> templates)
    {
        Describe("Prepared layout — refine the type, motion and alignment");
        await ui.OverviewAsync(video, 0.5);
        await ui.SelectAsync(Create, video);
        Describe("Create. — replace the placeholder");
        await ui.FramePropertyAsync(Root(Create).Id, "Text", video);
        await ui.TypePropertyAsync(Root(Create).Id, "Text", "Create.", video);
        Assert.That(((TextBlock)Root(Create)).Text.CurrentValue, Is.EqualTo("Create."));
        await CheckpointAsync("00-type");

        Describe("Add Clipping inside the effect group");
        await ui.AddClippingAsync(video);
        await CheckpointAsync("00-effect-group");
        var clipping = ((FilterEffectGroup)Root(Create).FilterEffect.CurrentValue!).Children.OfType<Clipping>().Single();
        await ui.FramePropertyAsync(clipping.Id, "Right", video);
        Describe("Drag Right until the text is hidden — set the starting pose first");
        await ui.ScrubAsync(0, video);
        await ui.ScrubPropertyAsync(clipping.Id, "Right", video, new(1380, 2.6), new(1436, 0.85));
        Assert.That(clipping.Right.Animation, Is.Null);
        Describe("Enable animation — the starting value becomes the first key");
        await ui.AddKeyframeAsync(clipping.Id, "Right", video);
        await video.HoldAsync(0.8);
        AssertKey(clipping, "Right", 0, 1436);
        Describe("At 0.10s, add a hold key with the same value");
        await AddHoldKeyAsync(Create, clipping, "Right", 0.1, 1436);
        Describe("At 0.88s, add a key and drag Right back to reveal the word");
        await ui.ScrubAsync(0.88, video);
        await ui.AddKeyframeAsync(clipping.Id, "Right", video);
        await ui.ScrubPropertyAsync(clipping.Id, "Right", video, new(56, 2.6), new(0, 0.9));
        AssertKey(clipping, "Right", 2, 0);
        Assert.That(ui.CapturePointer().Focus, Is.False, "Keyframe entry must keep the whole graph visible.");
        await CheckpointAsync("Right-keys");
        Describe("Fit the curve — add a midpoint for the reveal");
        await ui.FitGraphAsync(video);
        await CheckpointAsync("Create-graph-fit");
        await ui.ScrubAsync(0.49, video);
        await ui.AddKeyframeAsync(clipping.Id, "Right", video);
        Assert.That(((KeyFrameAnimation<float>)clipping.Right.Animation!).KeyFrames[2].Value, Is.EqualTo(718));
        Describe("Shape the reveal — use symmetric spline handles");
        await ui.PrepareSplineHandlesAsync(video, symmetric: true);
        await ui.DragSplineHandleAsync(2, false, 1f / 3, 0, video, symmetric: true);
        // The midpoint's outgoing handle follows its incoming handle in Symmetry mode.
        await ui.DragSplineHandleAsync(2, true, 2f / 3, 0, video, symmetric: true);
        await ui.DragSplineHandleAsync(3, true, 2f / 3, 1, video, symmetric: true);
        await CheckpointAsync("Create-spline-handles");
        AssertCursorMatchesReveal(clipping);
        await ReviewAsync("01-mask", 0, 1.5);

        Describe("Animate. — hold the word before the zoom");
        await ui.SelectAsync(Animate, video);
        await ui.ScrubAsync(3, video);
        var scale = (ScaleTransform)((TransformGroup)Root(Animate).Transform.CurrentValue!).Children[0];
        await ui.ShowTransformAsync(scale.Id, video);
        await ui.FramePropertyAsync(scale.Id, "Scale", video);
        // 100% is already the desired starting pose. Do not re-enter an unchanged value.
        Assert.That(scale.Scale.CurrentValue, Is.EqualTo(100));
        Assert.That(scale.Scale.Animation, Is.Null);
        Describe("Enable Scale animation from the current size");
        await ui.ScrubAsync(ui.Element(Animate).Start.TotalSeconds, video);
        await ui.AddKeyframeAsync(scale.Id, "Scale", video);
        await video.HoldAsync(0.8);
        Describe("Add a hold key before the transition");
        await AddHoldKeyAsync(Animate, scale, "Scale", 1.5, 100);
        Describe("Add a key while the word is visible — adjust Scale");
        await ui.ScrubAsync(ui.Element(Animate).Start.TotalSeconds + 2.0, video);
        await ui.AddKeyframeAsync(scale.Id, "Scale", video);
        await ui.ScrubPropertyAsync(scale.Id, "Scale", video, new(500, 2.3), new(475, 0.9));
        AssertKey(scale, "Scale", 2, 475);
        Assert.That(ui.CapturePointer().Focus, Is.False, "Keyframe entry must keep the whole graph visible.");
        await CheckpointAsync("Scale-keys");
        Describe("Move the ending key later — overlap the zoom with the wipe");
        await ui.MoveEndingKeyAsync(2.4, video);
        Describe("Shape the acceleration — drag the spline handles");
        await ui.FitGraphAsync(video);
        await CheckpointAsync("Animate-graph-fit");
        await ui.PrepareSplineHandlesAsync(video);
        await ui.DragSplineHandleAsync(2, false, 1f / 3, 0, video);
        await ui.DragSplineHandleAsync(2, true, 2f / 3, 0, video);
        await CheckpointAsync("Animate-spline-handles");
        await ReviewAsync("02-zoom", 2.1, 4.15);

        Describe("Make it yours. — align the word inside the card");
        await ui.SelectAsync(Yours, video);
        await ui.ScrubAsync(5.6, video);
        await ui.OverviewAsync(video, 0.9);
        await CheckpointAsync("03-card-before");
        var transforms = (TransformGroup)Root(Yours).Transform.CurrentValue!;
        var alignment = transforms.Children.OfType<TranslateTransform>().Single(t => t.Name == "Card alignment");
        await ui.ShowTransformAsync(alignment.Id, video);
        await ui.FramePropertyAsync(alignment.Id, "Y", video);
        Describe("Drag Y — compare the spacing above and below the word");
        await ui.ScrubPropertyAsync(alignment.Id, "Y", video, new(-8, 1.7), new(0, 0.9));
        Assert.That(alignment.Y.CurrentValue, Is.EqualTo(0).Within(0.001));
        Assert.That(alignment.Y.Animation, Is.Null);
        await ReviewAsync("03-card", 4.6, 6.5);

        await ui.ShowTimelineAsync(video);
        ui.ClearSelection();
        await ui.SeekAsync(7.9);
        Describe("Save and play — Create. Animate. Make it yours.");
        Assert.That(await ui.SaveAsync(), Is.True);
        await CheckpointAsync("04-finished");
        await ui.PlayAsync(video, 0, 8.4);
        await ui.OverviewAsync(video, 0.5);
        await File.WriteAllTextAsync(System.IO.Path.Combine(output, "workflow.json"),
            JsonSerializer.Serialize(new { Language = "en-US", StartingPoint = "Prepared typography layout", video.FrameRate, Steps = _steps },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task AddHoldKeyAsync(string element, EngineObject owner, string property, double time, float value)
    {
        await ui.ScrubAsync(ui.Element(element).Start.TotalSeconds + time, video);
        await ui.AddKeyframeAsync(owner.Id, property, video);
        AssertKey(owner, property, 1, value);
        await video.HoldAsync(0.9);
    }

    private void AssertCursorMatchesReveal(Clipping clipping)
    {
        var reveal = (KeyFrameAnimation<float>)clipping.Right.Animation!;
        var cursor = (TranslateTransform)((TransformGroup)Root("Create. / Blue cursor").Transform.CurrentValue!).Children[2];
        var cursorAnimation = (KeyFrameAnimation<float>)cursor.X.Animation!;
        // Supporting artwork is prepared with the final timing; no hidden cursor edit during the demo.
        for (int sample = 0; sample <= 100; sample++)
        {
            TimeSpan time = TimeSpan.FromSeconds(sample * 0.009);
            float cursorStart = Convert.ToSingle(cursorAnimation.KeyFrames[0].Value);
            float cursorEnd = Convert.ToSingle(cursorAnimation.KeyFrames.Last().Value);
            float progress = (cursorAnimation.Interpolate(time) - cursorStart) / (cursorEnd - cursorStart);
            Assert.That((1436 - reveal.Interpolate(time)) / 1436, Is.EqualTo(progress).Within(0.000001));
        }
    }

    private static void AssertKey(EngineObject owner, string property, int index, float value)
    {
        var animation = (KeyFrameAnimation)owner.Properties.Single(p => p.Name == property).Animation!;
        Assert.That(animation.KeyFrames.Count, Is.EqualTo(index + 1));
        Assert.That(Convert.ToSingle(animation.KeyFrames[index].Value, CultureInfo.InvariantCulture), Is.EqualTo(value).Within(0.001));
    }

    private Drawable Root(string name) => (Drawable)ui.Element(name).Objects[0];
    private Task CheckpointAsync(string name) => video.FrameAsync(System.IO.Path.Combine(output, name + ".png"));

    private async Task ReviewAsync(string checkpoint, double start, double end)
    {
        Describe("Play this section and check the result");
        await ui.PlayAsync(video, start, end);
        await CheckpointAsync(checkpoint);
        await video.HoldAsync(0.4);
    }

    private void Describe(string text)
    {
        TestContext.Progress.WriteLine($"Step at {video.FrameCount / (double)video.FrameRate:0.0}s: {text}");
        ui.Describe(text);
        _steps.Add(new { Frame = video.FrameCount, Action = text });
    }
}
