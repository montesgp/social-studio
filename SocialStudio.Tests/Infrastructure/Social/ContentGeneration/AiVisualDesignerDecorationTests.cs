using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.ContentGeneration;
using SocialStudio.Domain.Social.ProfileAnalysis;
using SocialStudio.Infrastructure.Social.ContentGeneration;

namespace SocialStudio.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Decoration must follow the CLIENT, not the house style.
///
/// <para>
/// The designer prompt used to carry an unconditional rule 9 — "build the background in layers …
/// one or two soft geometric shapes/blobs … a subtle shadow or glow" — which produced translucent
/// decorative circles on slides for a client whose measured corpus contains none. The aesthetic
/// extractor already reports `ImageTypeRatio.Decorator`; @atlas.editorial measured exactly 0
/// across 28 slides while the prompt kept ordering blobs anyway.
/// </para>
///
/// <para>
/// So the blob instruction is gated on the client's measured decorator usage, and its absence is
/// stated as an explicit prohibition rather than merely leaving the rule out — an omitted rule is
/// not an instruction, and the model happily decorates by default.
/// </para>
/// </summary>
public sealed class AiVisualDesignerDecorationTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task ClientThatNeverDecorates_IsToldNotToAddShapesOrGlow()
    {
        var prompt = await CapturePromptAsync(
            new ImageTypeRatio(Photo: 0.536, FlatColor: 0.464, Decorator: 0, SampleSize: 28));

        // The blob/glow order must be gone...
        Assert.DoesNotContain("soft geometric", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shapes/blobs", prompt, StringComparison.OrdinalIgnoreCase);

        // ...and replaced by an explicit prohibition, because silence is not an instruction.
        Assert.Contains("NO decorative", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("flat", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClientThatDoesDecorate_KeepsTheDepthDirection()
    {
        var prompt = await CapturePromptAsync(
            new ImageTypeRatio(Photo: 0.4, FlatColor: 0.2, Decorator: 0.4, SampleSize: 20));

        Assert.Contains("soft geometric", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NO decorative", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnmeasuredClient_KeepsTheExistingDepthDirection()
    {
        // Cold start: no ratio measured. Behaviour must be byte-identical to before this change,
        // so an unmeasured client is never silently stripped of the house design direction.
        var prompt = await CapturePromptAsync(imageTypeRatio: null);

        Assert.Contains("soft geometric", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NO decorative", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ANegligibleDecoratorShare_CountsAsNotDecorating()
    {
        // One stray decorator slide in a large corpus is noise, not a style.
        var prompt = await CapturePromptAsync(
            new ImageTypeRatio(Photo: 0.5, FlatColor: 0.48, Decorator: 0.02, SampleSize: 50));

        Assert.Contains("NO decorative", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnEmptySample_IsTreatedAsUnmeasured()
    {
        // Decorator = 0 out of a zero-slide sample proves nothing.
        var prompt = await CapturePromptAsync(
            new ImageTypeRatio(Photo: 0, FlatColor: 0, Decorator: 0, SampleSize: 0));

        Assert.Contains("soft geometric", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NO decorative", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static async Task<string> CapturePromptAsync(ImageTypeRatio? imageTypeRatio)
    {
        var gateway = new CapturePromptGateway();
        var designer = new AiVisualDesigner(gateway, new DecorationStubResolver());

        var aesthetic = new ClientAestheticDesignContext(
            ReferenceImages: [new ProfilePostImageRef("p1", 0, "org/c/a/aesthetic/p1-0.jpg", "h1")],
            Palette: [new AestheticPaletteEntry("#E1F03D", "primary", 1)],
            StyleDescriptors: ["flat saturated colour slides"],
            Motifs: ["handle watermark top-left"],
            VariationSeed: "job-1",
            BrandMark: null,
            ImageTypeRatio: imageTypeRatio);

        await designer.ComposeAsync(
            OrgId, BuildSpec(), BuildBrand(), new BackgroundSelection.Creative(),
            CancellationToken.None, aesthetic);

        Assert.NotNull(gateway.CapturedInput);
        return gateway.CapturedInput!;
    }

    private static VisualCompositionSpec BuildSpec() =>
        new(
            FormatKey: "feed-carousel",
            LayoutType: "carousel",
            Palette: new CompositionPalette("#111111", "#222222", "#E1F03D", "#FFFFFF", "#000000"),
            Typography: new CompositionTypography("Liberation Sans", 96, "Liberation Sans", 40),
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["headline"] = "Test" },
            Dimensions: new CompositionDimensions(1080, 1350, 96));

    private static BrandManualSnapshot BuildBrand() =>
        new(ColorPalette: [], Logos: [], BackgroundTemplates: [], Typography: [],
            VoiceAndTone: null, Notes: null);
}

file sealed class CapturePromptGateway : IAiCompletionGateway
{
    public string? CapturedInput { get; private set; }

    public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        CapturedInput = request.InputJson;
        var json = JsonSerializer.Serialize(new
        {
            markup = """<div class="composition" data-format="feed-carousel" style="width:1080px;height:1350px;"><div data-layer="background" style="background:#111;"></div><div data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3">Test</h1></div></div>"""
        });
        var payload = JsonSerializer.Deserialize<TResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return Task.FromResult(Result.Success(new AiJsonCompletionResponse<TResponse>(payload, "claude", "claude-opus-4-8", [], null)));
    }
}

file sealed class DecorationStubResolver : IWorkflowConfigurationResolver
{
    public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
        WorkflowStepResolutionRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
        [
            new WorkflowStepRouteCandidate(Guid.NewGuid(), "claude", Guid.NewGuid(),
                IntegrationConnectionMode.BringYourOwnKey, 1, "claude-opus-4-8", null, true)
        ]));
}
