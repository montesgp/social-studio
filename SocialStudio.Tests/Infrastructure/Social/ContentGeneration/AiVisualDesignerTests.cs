using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.ContentAnalysis;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.ContentGeneration;
using SocialStudio.Infrastructure.Social.ContentGeneration;

namespace SocialStudio.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// B1b TDD tests for <see cref="AiVisualDesigner"/>.
/// Tasks: 8.1 — mock AI gateway; no real AI designer call.
/// Design: Decision 5 (AI-as-Designer), 9 (PromptVersionKey deterministic const).
/// NOTE: Tier-2 multimodal (BackgroundSelection.References image input) is a follow-up.
///       This test suite marks the IMAGE-INPUT path as "follow-up flagged" per the apply instructions.
/// </summary>
public sealed class AiVisualDesignerTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // -----------------------------------------------------------------------
    // Task 8.1 RED: IsActive = true
    // -----------------------------------------------------------------------

    [Fact]
    public void AiVisualDesigner_IsActive_ReturnsTrue()
    {
        var designer = BuildDesigner(new StubAiGateway(BuildSuccessMarkup()));
        Assert.True(designer.IsActive,
            "AiVisualDesigner.IsActive must return true — it is the real designer that replaces NoOpVisualDesigner.");
    }

    // -----------------------------------------------------------------------
    // Task 8.1 RED: successful ComposeAsync — mock gateway returns valid HTML
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_MockGateway_ReturnsValidOutput()
    {
        // GIVEN a mock gateway that returns valid constrained HTML
        var expectedMarkup = BuildSuccessMarkup();
        var designer = BuildDesigner(new StubAiGateway(expectedMarkup));

        var spec = BuildSpec();
        var brand = BuildBrand();
        var selection = new BackgroundSelection.Creative();

        // WHEN ComposeAsync is called
        var result = await designer.ComposeAsync(OrgId, spec, brand, selection, CancellationToken.None);

        // THEN result is successful
        Assert.True(result.IsSuccess, $"Expected success but got: {(result.IsFailure ? result.Error.MessageKey : "n/a")}");

        var output = result.Value;
        Assert.False(string.IsNullOrWhiteSpace(output.Markup), "Markup must be non-empty.");
        Assert.Equal("visual-designer/v2", output.PromptVersionKey);
        Assert.False(output.IsValid, "IsValid must be false — guardrails have not run yet.");
    }

    // -----------------------------------------------------------------------
    // Task 8.1 RED: PromptVersionKey is the deterministic const
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_PromptVersionKey_IsDeterministicConst()
    {
        var designer = BuildDesigner(new StubAiGateway(BuildSuccessMarkup()));
        var result = await designer.ComposeAsync(
            OrgId, BuildSpec(), BuildBrand(), new BackgroundSelection.Creative(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("visual-designer/v2", result.Value.PromptVersionKey);
    }

    // -----------------------------------------------------------------------
    // Task 8.1 RED: gateway failure → ComposeAsync returns failure Result
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_GatewayFailure_ReturnsFailedResult()
    {
        // GIVEN a mock gateway that returns failure
        var designer = BuildDesigner(new FailingAiGateway());

        var result = await designer.ComposeAsync(
            OrgId, BuildSpec(), BuildBrand(), new BackgroundSelection.Creative(), CancellationToken.None);

        Assert.True(result.IsFailure, "Gateway failure must propagate as failed Result.");
    }

    // -----------------------------------------------------------------------
    // Task 8.1 RED: Tier-1 ForcedDefault — gateway receives background URL in prompt
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_Tier1ForcedDefault_PromptContainsBgUrl()
    {
        // GIVEN a gateway spy that captures the prompt text
        var gatewaySpy = new SpyAiGateway(BuildSuccessMarkup());
        var designer = BuildDesigner(gatewaySpy);

        var forcedDefaultSelection = new BackgroundSelection.ForcedDefault(
            Url: "https://cdn.example.com/forced-bg.png",
            Name: "forced-story-bg");

        await designer.ComposeAsync(OrgId, BuildSpec(), BuildBrand(), forcedDefaultSelection, CancellationToken.None);

        // THEN the prompt JSON must contain the forced background URL
        Assert.NotNull(gatewaySpy.CapturedInput);
        Assert.Contains("https://cdn.example.com/forced-bg.png", gatewaySpy.CapturedInput,
            StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Task 8.1 RED: Tier-2 References — FOLLOW-UP FLAG
    // NOTE: Multimodal image input for Tier-2 References is a follow-up.
    //       The gateway's CompleteJsonAsync accepts only text today (no vision blocks).
    //       This test documents the gap and verifies the designer still succeeds (text-only fallback).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_Tier2References_TextOnlyFallback_Succeeds()
    {
        // GIVEN a Tier-2 reference background selection
        var references = new BackgroundSelection.References(new[]
        {
            new BrandBackgroundTemplateSnapshot(
                "ref-bg-1", "https://cdn.example.com/ref1.png", null,
                BackgroundTier.Reference, "story", false)
        });

        var designer = BuildDesigner(new StubAiGateway(BuildSuccessMarkup()));

        // WHEN ComposeAsync runs — should succeed with text-only guidance (no vision block)
        var result = await designer.ComposeAsync(
            OrgId, BuildSpec(), BuildBrand(), references, CancellationToken.None);

        // THEN it succeeds (text-only fallback; vision is a follow-up)
        Assert.True(result.IsSuccess,
            "Tier-2 References must succeed with text-only fallback. " +
            "FOLLOW-UP: Add vision image blocks when IAiCompletionGateway supports multimodal input.");
    }

    // -----------------------------------------------------------------------
    // Overlay fix: image-background tiers must forbid the full-bleed scrim/overlay
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_Tier1ForcedDefault_PromptForbidsFullBleedScrim()
    {
        var gatewaySpy = new SpyAiGateway(BuildSuccessMarkup());
        var designer = BuildDesigner(gatewaySpy);
        var forcedDefault = new BackgroundSelection.ForcedDefault(
            Url: "https://cdn.example.com/forced-bg.png", Name: "forced-bg");

        await designer.ComposeAsync(OrgId, BuildSpec(), BuildBrand(), forcedDefault, CancellationToken.None);

        Assert.NotNull(gatewaySpy.CapturedInput);
        Assert.Contains("DO NOT lay a full-bleed translucent scrim", gatewaySpy.CapturedInput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComposeAsync_Tier2References_PromptForbidsFullBleedScrim()
    {
        var gatewaySpy = new SpyAiGateway(BuildSuccessMarkup());
        var designer = BuildDesigner(gatewaySpy);
        var references = new BackgroundSelection.References(new[]
        {
            new BrandBackgroundTemplateSnapshot(
                "ref-bg-1", "https://cdn.example.com/ref1.png", null,
                BackgroundTier.Reference, "story", false)
        });

        await designer.ComposeAsync(OrgId, BuildSpec(), BuildBrand(), references, CancellationToken.None);

        Assert.NotNull(gatewaySpy.CapturedInput);
        Assert.Contains("DO NOT lay a full-bleed translucent scrim", gatewaySpy.CapturedInput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComposeAsync_CreativeTier_DoesNotForbidScrim()
    {
        // The no-scrim rule is scoped to real image backgrounds; the creative tier generates its own
        // background and may legitimately use overlays, so the directive must not leak into it.
        var gatewaySpy = new SpyAiGateway(BuildSuccessMarkup());
        var designer = BuildDesigner(gatewaySpy);

        await designer.ComposeAsync(OrgId, BuildSpec(), BuildBrand(), new BackgroundSelection.Creative(), CancellationToken.None);

        Assert.NotNull(gatewaySpy.CapturedInput);
        Assert.DoesNotContain("DO NOT lay a full-bleed translucent scrim", gatewaySpy.CapturedInput, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // R4.5 (Content Studio editing completeness, Decision D4): the prompt no longer licenses an
    // arbitrary system fallback font — the guardrail (R4.3) now hard-rejects anything the prompt and
    // the guardrail would otherwise contradict on.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_PromptTypographyInstruction_DoesNotLicenseSystemFallback()
    {
        var gatewaySpy = new SpyAiGateway(BuildSuccessMarkup());
        var designer = BuildDesigner(gatewaySpy);

        await designer.ComposeAsync(OrgId, BuildSpec(), BuildBrand(), new BackgroundSelection.Creative(), CancellationToken.None);

        Assert.NotNull(gatewaySpy.CapturedInput);
        Assert.DoesNotContain("a clean system fallback is acceptable", gatewaySpy.CapturedInput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComposeAsync_PromptTypographyInstruction_RequiresBrandFamilyVerbatim()
    {
        var gatewaySpy = new SpyAiGateway(BuildSuccessMarkup());
        var designer = BuildDesigner(gatewaySpy);

        await designer.ComposeAsync(OrgId, BuildSpec(), BuildBrand(), new BackgroundSelection.Creative(), CancellationToken.None);

        Assert.NotNull(gatewaySpy.CapturedInput);
        // The prompt must instruct the model to use the brand family verbatim (or a CSS generic,
        // which the pipeline normalizes) — never an invented, unrelated named font.
        Assert.Contains("TYPOGRAPHY", gatewaySpy.CapturedInput, StringComparison.Ordinal);
        Assert.Contains("verbatim", gatewaySpy.CapturedInput, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static AiVisualDesigner BuildDesigner(IAiCompletionGateway gateway) =>
        new AiVisualDesigner(gateway, new StubWorkflowResolver());

    private static VisualCompositionSpec BuildSpec() =>
        new VisualCompositionSpec(
            FormatKey: "story",
            LayoutType: "story-story",
            Palette: new CompositionPalette("#111111", "#222222", "#3B82F6", "#FFFFFF", "#F9FAFB"),
            Typography: new CompositionTypography("Inter", 24, "Inter", 14),
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string>
            {
                ["headline"] = "Your Brand Headline",
                ["subtitle"] = "A compelling subtitle"
            },
            Dimensions: new CompositionDimensions(1080, 1920, 96));

    private static BrandManualSnapshot BuildBrand() =>
        new BrandManualSnapshot(
            ColorPalette: new[]
            {
                new WeightedColor("#111111", ColorRole.Primary,   0),
                new WeightedColor("#222222", ColorRole.Secondary, 1),
                new WeightedColor("#3B82F6", ColorRole.Accent,    2),
                new WeightedColor("#FFFFFF", ColorRole.Neutral,   3),
                new WeightedColor("#F9FAFB", null,                4),
            },
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: new[] { "Inter" },
            VoiceAndTone: "Professional and modern",
            Notes: null);

    private static string BuildSuccessMarkup() =>
        """<div class="composition" data-format="story" style="width:1080px;height:1920px;background:#111111;"><div class="layer-background" data-layer="background" style="background:#111111;"></div><div class="layer-content" data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="font-family:Inter;color:#FFFFFF;font-size:48px;">Your Brand Headline</h1></div></div>""";
}

/// <summary>Stub AI selector — mirrors the org's configured AI (provider + model) for the designer.</summary>
file sealed class StubWorkflowResolver : IWorkflowConfigurationResolver
{
    public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
        WorkflowStepResolutionRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
        [
            new WorkflowStepRouteCandidate(Guid.NewGuid(), "claude", Guid.NewGuid(),
                IntegrationConnectionMode.BringYourOwnKey, 1, "claude-sonnet-4-6", null, true)
        ]));
}

// -----------------------------------------------------------------------
// Gateway stubs — no real HTTP/Anthropic call
// -----------------------------------------------------------------------

file sealed class StubAiGateway(string markupToReturn) : IAiCompletionGateway
{
    public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        // Return the markup string wrapped in whatever TResponse shape is expected.
        // For AiVisualDesigner, TResponse = AiVisualDesignResponse (a simple wrapper record).
        // We serialize markupToReturn into a JSON string the gateway would return.
        var json = JsonSerializer.Serialize(new { markup = markupToReturn });
        TResponse payload;
        try
        {
            payload = JsonSerializer.Deserialize<TResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        catch
        {
            // If TResponse is not deserializable from this JSON, return failure
            return Task.FromResult(Result.Failure<AiJsonCompletionResponse<TResponse>>(
                Error.Failure("stub.deserialize-failed", "Stub could not produce TResponse.")));
        }

        var response = new AiJsonCompletionResponse<TResponse>(
            payload,
            ProviderKey: "claude",
            Model: "claude-opus-4-5",
            Diagnostics: [],
            TokenUsage: null);

        return Task.FromResult(Result.Success(response));
    }
}

file sealed class FailingAiGateway : IAiCompletionGateway
{
    public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        return Task.FromResult(Result.Failure<AiJsonCompletionResponse<TResponse>>(
            Error.Failure("ai.unavailable", "Simulated gateway failure.")));
    }
}

file sealed class SpyAiGateway(string markupToReturn) : IAiCompletionGateway
{
    public string? CapturedInput { get; private set; }

    public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        CapturedInput = request.InputJson;

        var json = JsonSerializer.Serialize(new { markup = markupToReturn });
        TResponse payload;
        try
        {
            payload = JsonSerializer.Deserialize<TResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        catch
        {
            return Task.FromResult(Result.Failure<AiJsonCompletionResponse<TResponse>>(
                Error.Failure("spy.deserialize-failed", "Spy could not produce TResponse.")));
        }

        var response = new AiJsonCompletionResponse<TResponse>(
            payload,
            ProviderKey: "claude",
            Model: "claude-opus-4-5",
            Diagnostics: [],
            TokenUsage: null);

        return Task.FromResult(Result.Success(response));
    }
}
