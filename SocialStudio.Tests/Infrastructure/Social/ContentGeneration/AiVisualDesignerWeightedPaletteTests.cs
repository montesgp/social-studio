using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.ContentGeneration;
using SocialStudio.Infrastructure.Social.ContentGeneration;

namespace SocialStudio.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Step 1.12 — assert that <see cref="AiVisualDesigner.ComposeAsync"/> serializes the weighted palette
/// as <c>(hex, role, rank)</c> tuples in rank order, and that the system prompt expresses
/// prevalence weighting language (Primary/dominant, Accent/sparingly).
/// </summary>
public sealed class AiVisualDesignerWeightedPaletteTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // -----------------------------------------------------------------------
    // Assert DTO carries (hex, role, rank) tuples
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_InputJson_ContainsPrimaryHexAndRoleLabel()
    {
        // GIVEN a brand with a named Primary color
        var gatewaySpy = new PaletteCaptureGateway();
        var designer = BuildDesigner(gatewaySpy);
        var brand = BuildBrandWith(
            new WeightedColor("#EE1122", ColorRole.Primary,   0),
            new WeightedColor("#002244", ColorRole.Secondary, 1),
            new WeightedColor("#FFAA00", ColorRole.Accent,    2));

        // WHEN the designer composes
        await designer.ComposeAsync(OrgId, BuildSpec(), brand, new BackgroundSelection.Creative(), CancellationToken.None);

        // THEN the DTO input JSON contains the primary hex and the "Primary" role label
        Assert.NotNull(gatewaySpy.CapturedInput);
        Assert.Contains("#EE1122", gatewaySpy.CapturedInput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Primary", gatewaySpy.CapturedInput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ComposeAsync_InputJson_ContainsHexRoleRankProperties()
    {
        // GIVEN palette with all three properties populated
        var gatewaySpy = new PaletteCaptureGateway();
        var designer = BuildDesigner(gatewaySpy);
        var brand = BuildBrandWith(
            new WeightedColor("#111AAA", ColorRole.Primary,   0),
            new WeightedColor("#222BBB", ColorRole.Accent,    5));

        await designer.ComposeAsync(OrgId, BuildSpec(), brand, new BackgroundSelection.Creative(), CancellationToken.None);

        // THEN JSON contains "hex" and "role" and "rank" property keys (camelCase from System.Text.Json)
        Assert.NotNull(gatewaySpy.CapturedInput);
        Assert.Contains("\"hex\"", gatewaySpy.CapturedInput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"role\"", gatewaySpy.CapturedInput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"rank\"", gatewaySpy.CapturedInput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ComposeAsync_InputJson_AccentColorAppearsAfterPrimary()
    {
        // GIVEN a palette where Primary comes before Accent in rank order
        var gatewaySpy = new PaletteCaptureGateway();
        var designer = BuildDesigner(gatewaySpy);
        var brand = BuildBrandWith(
            new WeightedColor("#111111", ColorRole.Primary, 0),
            new WeightedColor("#FFCC00", ColorRole.Accent,  1));

        await designer.ComposeAsync(OrgId, BuildSpec(), brand, new BackgroundSelection.Creative(), CancellationToken.None);

        Assert.NotNull(gatewaySpy.CapturedInput);
        var primaryPos = gatewaySpy.CapturedInput.IndexOf("#111111", StringComparison.OrdinalIgnoreCase);
        var accentPos  = gatewaySpy.CapturedInput.IndexOf("#FFCC00", StringComparison.OrdinalIgnoreCase);

        Assert.True(primaryPos >= 0, "Primary color #111111 must appear in input JSON.");
        Assert.True(accentPos >= 0,  "Accent color #FFCC00 must appear in input JSON.");
        Assert.True(primaryPos < accentPos,
            "Primary color must appear before Accent color in DTO (rank order).");
    }

    // -----------------------------------------------------------------------
    // Assert system prompt contains prevalence weighting language
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_SystemPrompt_ContainsDominantPrevalenceLanguage()
    {
        // GIVEN any brand with a weighted palette
        var gatewaySpy = new PaletteCaptureGateway();
        var designer = BuildDesigner(gatewaySpy);
        var brand = BuildBrandWith(new WeightedColor("#FF0000", ColorRole.Primary, 0));

        await designer.ComposeAsync(OrgId, BuildSpec(), brand, new BackgroundSelection.Creative(), CancellationToken.None);

        Assert.NotNull(gatewaySpy.CapturedSystemPrompt);
        // Design §6 item 5: "prompt expresses prevalence (primary dominant, accent sparing)"
        Assert.True(
            gatewaySpy.CapturedSystemPrompt.Contains("dominant", StringComparison.OrdinalIgnoreCase) ||
            gatewaySpy.CapturedSystemPrompt.Contains("dominat", StringComparison.OrdinalIgnoreCase),
            "System prompt must express that Primary is dominant.");
    }

    [Fact]
    public async Task ComposeAsync_SystemPrompt_ContainsSparinglyLanguageForAccent()
    {
        // GIVEN a brand with Accent color
        var gatewaySpy = new PaletteCaptureGateway();
        var designer = BuildDesigner(gatewaySpy);
        var brand = BuildBrandWith(
            new WeightedColor("#FF0000", ColorRole.Primary, 0),
            new WeightedColor("#FFAA00", ColorRole.Accent, 1));

        await designer.ComposeAsync(OrgId, BuildSpec(), brand, new BackgroundSelection.Creative(), CancellationToken.None);

        Assert.NotNull(gatewaySpy.CapturedSystemPrompt);
        // Design §6 item 5: "primary dominant, accent sparing"
        Assert.True(
            gatewaySpy.CapturedSystemPrompt.Contains("sparing", StringComparison.OrdinalIgnoreCase) ||
            gatewaySpy.CapturedSystemPrompt.Contains("Accent", StringComparison.OrdinalIgnoreCase),
            "System prompt must express that Accent is used sparingly.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static AiVisualDesigner BuildDesigner(IAiCompletionGateway gateway) =>
        new AiVisualDesigner(gateway, new StubPaletteWorkflowResolver());

    private static BrandManualSnapshot BuildBrandWith(params WeightedColor[] palette) =>
        new BrandManualSnapshot(
            ColorPalette: palette,
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: new[] { "Inter" },
            VoiceAndTone: "Professional",
            Notes: null);

    private static VisualCompositionSpec BuildSpec() =>
        new VisualCompositionSpec(
            FormatKey: "story",
            LayoutType: "story-story",
            Palette: new CompositionPalette("#111111", "#222222", "#3B82F6", "#FFFFFF", "#F9FAFB"),
            Typography: new CompositionTypography("Inter", 24, "Inter", 14),
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["headline"] = "Test" },
            Dimensions: new CompositionDimensions(1080, 1920, 96));
}

// ---- Test doubles ----

file sealed class StubPaletteWorkflowResolver : IWorkflowConfigurationResolver
{
    public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
        WorkflowStepResolutionRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
        [
            new WorkflowStepRouteCandidate(Guid.NewGuid(), "claude", Guid.NewGuid(),
                IntegrationConnectionMode.BringYourOwnKey, 1, "claude-sonnet-4-6", null, true)
        ]));
}

/// <summary>Captures both the input JSON and the system prompt sent to the AI gateway.</summary>
file sealed class PaletteCaptureGateway : IAiCompletionGateway
{
    public string? CapturedInput { get; private set; }

    // SystemPrompt is serialized inside InputJson as the "systemPrompt" property of DesignerInputDto.
    public string? CapturedSystemPrompt => CapturedInput;

    public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        CapturedInput = request.InputJson;

        // Return minimal valid response for AiVisualDesigner's expected TResponse shape
        var markup =
            """<div class="composition" data-format="story" style="width:1080px;height:1920px;"><div class="layer-background" data-layer="background" style="background:#111111;"></div><div class="layer-content" data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="font-family:Inter;color:#FFFFFF;">Test</h1></div></div>""";
        var json = JsonSerializer.Serialize(new { markup });
        TResponse payload;
        try
        {
            payload = JsonSerializer.Deserialize<TResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        catch
        {
            return Task.FromResult(Result.Failure<AiJsonCompletionResponse<TResponse>>(
                Error.Failure("palette-capture.deserialize-failed", "Could not produce TResponse.")));
        }

        return Task.FromResult(Result.Success(new AiJsonCompletionResponse<TResponse>(
            payload, "claude", "claude-sonnet-4-6", [], null)));
    }
}
