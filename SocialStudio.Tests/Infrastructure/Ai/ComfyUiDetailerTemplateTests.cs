using System.Text.Json;
using SocialStudio.Infrastructure.Ai.Providers;

namespace SocialStudio.Tests.Infrastructure.Ai;

/// <summary>
/// WU-2 RED/GREEN tests (task 2.1/2.2): the detailer-augmented workflow template
/// (<c>DefaultComfyUiWorkflowTemplateWithDetailer.json</c>) must expose the same placeholder-token
/// set as the plain template and splice a <c>FaceDetailer</c>/<c>UltralyticsDetectorProvider</c>
/// pair between <c>VAEDecode</c> and <c>SaveImage</c>. Refs: A6, design §Interfaces/Contracts.
/// </summary>
public sealed class ComfyUiDetailerTemplateTests
{
    private static readonly string[] ExpectedTokens =
    [
        "CHECKPOINT_NAME",
        "POSITIVE_PROMPT",
        "NEGATIVE_PROMPT",
        "REFERENCE_IMAGE",
        "WIDTH",
        "HEIGHT",
        "SEED",
        "STEPS",
        "CFG",
    ];

    [Fact]
    public void AugmentedTemplate_PlaceholderTokenSet_MatchesPlainTemplate()
    {
        var plainTokens = ExtractTokens(LoadEmbeddedResource("DefaultComfyUiWorkflowTemplate.json"));
        var augmentedTokens = ExtractTokens(LoadEmbeddedResource("DefaultComfyUiWorkflowTemplateWithDetailer.json"));

        Assert.Equal(plainTokens, augmentedTokens);
        Assert.Equal(ExpectedTokens.ToHashSet(), augmentedTokens);
    }

    [Fact]
    public void AugmentedTemplate_SaveImage_ConsumesFaceDetailerOutput()
    {
        var template = LoadEmbeddedResource("DefaultComfyUiWorkflowTemplateWithDetailer.json");
        var substituted = template
            .Replace("{{CHECKPOINT_NAME}}", "checkpoint.safetensors", StringComparison.Ordinal)
            .Replace("{{POSITIVE_PROMPT}}", "prompt", StringComparison.Ordinal)
            .Replace("{{NEGATIVE_PROMPT}}", "", StringComparison.Ordinal)
            .Replace("{{REFERENCE_IMAGE}}", "ref.png", StringComparison.Ordinal)
            .Replace("{{WIDTH}}", "512", StringComparison.Ordinal)
            .Replace("{{HEIGHT}}", "512", StringComparison.Ordinal)
            .Replace("{{SEED}}", "1", StringComparison.Ordinal)
            .Replace("{{STEPS}}", "20", StringComparison.Ordinal)
            .Replace("{{CFG}}", "7.0", StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(substituted);

        var saveImageInputs = doc.RootElement.GetProperty("9").GetProperty("inputs");
        var imagesEdge = saveImageInputs.GetProperty("images");
        Assert.Equal("11", imagesEdge[0].GetString());
        Assert.Equal(0, imagesEdge[1].GetInt32());

        Assert.Equal(
            "UltralyticsDetectorProvider",
            doc.RootElement.GetProperty("10").GetProperty("class_type").GetString());
        Assert.Equal(
            "FaceDetailer",
            doc.RootElement.GetProperty("11").GetProperty("class_type").GetString());

        var faceDetailerInputs = doc.RootElement.GetProperty("11").GetProperty("inputs");
        var modelEdge = faceDetailerInputs.GetProperty("model");
        Assert.Equal("6", modelEdge[0].GetString());
        Assert.Equal(0, modelEdge[1].GetInt32());

        var bboxDetectorEdge = faceDetailerInputs.GetProperty("bbox_detector");
        Assert.Equal("10", bboxDetectorEdge[0].GetString());

        // steps/cfg on the detailer node are literals, not substitution tokens.
        Assert.Equal(20, faceDetailerInputs.GetProperty("steps").GetInt32());
        Assert.Equal(7.0, faceDetailerInputs.GetProperty("cfg").GetDouble());
    }

    private static string SubstituteDummyTokens(string template) => template
        .Replace("{{CHECKPOINT_NAME}}", "checkpoint.safetensors", StringComparison.Ordinal)
        .Replace("{{POSITIVE_PROMPT}}", "prompt", StringComparison.Ordinal)
        .Replace("{{NEGATIVE_PROMPT}}", "", StringComparison.Ordinal)
        .Replace("{{REFERENCE_IMAGE}}", "ref.png", StringComparison.Ordinal)
        .Replace("{{WIDTH}}", "512", StringComparison.Ordinal)
        .Replace("{{HEIGHT}}", "512", StringComparison.Ordinal)
        .Replace("{{SEED}}", "1", StringComparison.Ordinal)
        .Replace("{{STEPS}}", "20", StringComparison.Ordinal)
        .Replace("{{CFG}}", "7.0", StringComparison.Ordinal);

    private static HashSet<string> ExtractTokens(string template)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(template, @"\{\{(\w+)\}\}");
        return matches.Select(m => m.Groups[1].Value).ToHashSet();
    }

    // -----------------------------------------------------------------------
    // Real-generation fix (2026-08-27): a slide whose imagePromptHint asked for NO person at all
    // still came out with a duplicated/mutated face. The IPAdapter reference-image conditioning
    // (node "6", shared by both templates) was the only mechanism injecting facial identity into
    // that composition. weight=0.4 across the first half of the denoise trajectory (end_at=0.5) is
    // a documented IPAdapter cause of duplicate-identity artifacts when the base generation
    // independently produces its own subject. Lowering both is a zero-extra-VRAM lever (no new
    // node, no extra pass) — reduces conditioning strength/window without removing IPAdapter
    // entirely, at the cost of weaker resemblance to the reference photo.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("DefaultComfyUiWorkflowTemplate.json")]
    [InlineData("DefaultComfyUiWorkflowTemplateWithDetailer.json")]
    public void IpAdapterAdvanced_WeightAndWindow_AreConservative(string fileName)
    {
        var substituted = SubstituteDummyTokens(LoadEmbeddedResource(fileName));
        using var doc = JsonDocument.Parse(substituted);
        var inputs = doc.RootElement.GetProperty("6").GetProperty("inputs");

        Assert.Equal(0.3, inputs.GetProperty("weight").GetDouble());
        Assert.Equal(0.0, inputs.GetProperty("start_at").GetDouble());
        Assert.Equal(0.35, inputs.GetProperty("end_at").GetDouble());
    }

    private static string LoadEmbeddedResource(string fileName)
    {
        var resourceName = $"SocialStudio.Infrastructure.Ai.Providers.{fileName}";
        var assembly = typeof(ComfyUiImageModelClient).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
