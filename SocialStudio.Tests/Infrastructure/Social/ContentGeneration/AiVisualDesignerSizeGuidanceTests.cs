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
/// client-aesthetic-generation-hardening WU-2 (CAGH-CG-02, D2): <c>AiVisualDesigner.BuildSystemPrompt</c>'s
/// headline size-guidance line is derived from the client's resolved <c>MeasuredHeadingSize</c> when
/// present, rounded to the nearest whole percent of canvas height. Unmeasured clients keep the
/// pre-existing fixed <c>7-13%</c> guidance line verbatim (regression, CAGH-CG-02-b).
/// </summary>
public sealed class AiVisualDesignerSizeGuidanceTests
{
    private static readonly Guid OrgId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task MeasuredHeadingSize_DrivesSizeGuidanceLine_NotTheFixedRange()
    {
        // 154px heading on a 1920px canvas -> round(154*100/1920) = 8%
        var gatewaySpy = new SizeGuidanceCaptureGateway();
        var designer = BuildDesigner(gatewaySpy);
        var brand = BuildBrand();
        var spec = BuildSpec(measuredHeadingSize: 154, canvasHeight: 1920);

        await designer.ComposeAsync(OrgId, spec, brand, new BackgroundSelection.Creative(), CancellationToken.None);

        Assert.NotNull(gatewaySpy.CapturedSystemPrompt);
        Assert.Contains("about 8% of canvas height", gatewaySpy.CapturedSystemPrompt);
        Assert.DoesNotContain("7-13% of canvas height", gatewaySpy.CapturedSystemPrompt);
    }

    [Fact]
    public async Task NoMeasuredHeadingSize_PreservesFixedGuidanceLineVerbatim()
    {
        var gatewaySpy = new SizeGuidanceCaptureGateway();
        var designer = BuildDesigner(gatewaySpy);
        var brand = BuildBrand();
        var spec = BuildSpec(measuredHeadingSize: null, canvasHeight: 1920);

        await designer.ComposeAsync(OrgId, spec, brand, new BackgroundSelection.Creative(), CancellationToken.None);

        Assert.NotNull(gatewaySpy.CapturedSystemPrompt);
        Assert.Contains("about 7-13% of canvas height", gatewaySpy.CapturedSystemPrompt);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static AiVisualDesigner BuildDesigner(IAiCompletionGateway gateway) =>
        new AiVisualDesigner(gateway, new SizeGuidanceStubPaletteWorkflowResolver());

    private static BrandManualSnapshot BuildBrand() =>
        new BrandManualSnapshot(
            ColorPalette: [new WeightedColor("#111111", ColorRole.Primary, 0)],
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: new[] { "Inter" },
            VoiceAndTone: "Professional",
            Notes: null);

    private static VisualCompositionSpec BuildSpec(int? measuredHeadingSize, int canvasHeight) =>
        new VisualCompositionSpec(
            FormatKey: "story",
            LayoutType: "story-story",
            Palette: new CompositionPalette("#111111", "#222222", "#3B82F6", "#FFFFFF", "#F9FAFB"),
            Typography: new CompositionTypography("Inter", 24, "Inter", 14) { MeasuredHeadingSize = measuredHeadingSize },
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["headline"] = "Test" },
            Dimensions: new CompositionDimensions(1080, canvasHeight, 96));
}

// ---- Test doubles ----

file sealed class SizeGuidanceStubPaletteWorkflowResolver : IWorkflowConfigurationResolver
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
file sealed class SizeGuidanceCaptureGateway : IAiCompletionGateway
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
                Error.Failure("size-guidance-capture.deserialize-failed", "Could not produce TResponse.")));
        }

        return Task.FromResult(Result.Success(new AiJsonCompletionResponse<TResponse>(
            payload, "claude", "claude-sonnet-4-6", [], null)));
    }
}
