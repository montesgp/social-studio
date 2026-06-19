using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Caption;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.ContentIdea;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.CtaVariation;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Story;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Hook;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.ReelScript;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Formats;

/// <summary>
/// TDD tests for Story, ReelScript, ContentIdea, CtaVariation, Caption, Hook generators.
/// RED phase: references types that don't exist yet.
/// Each format has:
///   - Success returns correct status + prompt version + format key
///   - AI failure returns failed status
///   - GenerateBoxedAsync boxes correctly
/// </summary>
public sealed class RemainingFormatsGeneratorTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid AnalysisId = Guid.NewGuid();
    private static readonly Guid CorrelationId = Guid.NewGuid();

    // ================================================================
    // STORY
    // ================================================================

    [Fact]
    public async Task StoryGenerator_Success_ReturnsSucceededWithStoryContent()
    {
        var story = new StoryContent(
            "Big Title",
            "Subtitle",
            "Body text",
            ["Feature 1", "Feature 2"],
            "Contact us",
            "Branded product background",
            "@client",
            "grid");
        var gateway = new SucceedingGateway<StoryContent>(story);
        var ctx = BuildContext();
        var providerCtx = BeginContext("format:story");
        var generator = new StoryGenerator(gateway, providerCtx);

        var result = await generator.GenerateAsync(ctx, CancellationToken.None);

        Assert.Equal(FormatGenerationStatus.Succeeded, result.Status);
        Assert.Equal(story.Title, result.Payload!.Title);
        Assert.Equal(ContentGenerationPromptVersions.Story, result.PromptVersion);
        Assert.NotNull(gateway.LastRequest);
        Assert.Contains("publicationDescription", gateway.LastRequest!.InputJson);
    }

    [Fact]
    public void StoryGenerator_FormatKeyAndPromptVersion_AreCorrect()
    {
        var gen = new StoryGenerator(new SucceedingGateway<StoryContent>(BuildStory()), BeginContext("format:story"));
        Assert.Equal("story-single", gen.FormatKey);
        Assert.Equal(ContentGenerationPromptVersions.Story, gen.PromptVersion);
    }

    [Fact]
    public async Task StoryGenerator_AiFailure_ReturnsFailedResult()
    {
        var gateway = new FailingGateway<StoryContent>(Error.Failure("ai.fail", "fail"));
        var gen = new StoryGenerator(gateway, BeginContext("format:story"));
        var result = await gen.GenerateAsync(BuildContext(), CancellationToken.None);
        Assert.Equal(FormatGenerationStatus.Failed, result.Status);
    }

    // ================================================================
    // REEL SCRIPT
    // ================================================================

    [Fact]
    public async Task ReelScriptGenerator_Success_ReturnsSucceededWithReelScriptContent()
    {
        var reelScript = new ReelScriptContent(
            Hook: "Did you know...?",
            Scenes: [new ReelScriptScene("0:00", "Voiceover text", "On-screen text")],
            CtaText: "Follow for more",
            SuggestedMusicMood: "upbeat");
        var gateway = new SucceedingGateway<ReelScriptContent>(reelScript);
        var gen = new ReelScriptGenerator(gateway, BeginContext("format:reel-script"));

        var result = await gen.GenerateAsync(BuildContext(), CancellationToken.None);

        Assert.Equal(FormatGenerationStatus.Succeeded, result.Status);
        Assert.Equal(reelScript.Hook, result.Payload!.Hook);
        Assert.Equal("reel-script.v2", result.PromptVersion);
    }

    [Fact]
    public void ReelScriptGenerator_FormatKeyAndPromptVersion_AreCorrect()
    {
        var gen = new ReelScriptGenerator(new SucceedingGateway<ReelScriptContent>(BuildReelScript()), BeginContext("format:reel-script"));
        Assert.Equal("reel-script", gen.FormatKey);
        Assert.Equal(ContentGenerationPromptVersions.ReelScript, gen.PromptVersion);
    }

    [Fact]
    public async Task ReelScriptGenerator_AiFailure_ReturnsFailedResult()
    {
        var gen = new ReelScriptGenerator(new FailingGateway<ReelScriptContent>(Error.Failure("fail", "fail")), BeginContext("format:reel-script"));
        var result = await gen.GenerateAsync(BuildContext(), CancellationToken.None);
        Assert.Equal(FormatGenerationStatus.Failed, result.Status);
    }

    // ================================================================
    // CONTENT IDEA
    // ================================================================

    [Fact]
    public async Task ContentIdeaGenerator_Success_ReturnsSucceededWithContentIdeaContent()
    {
        var idea = new ContentIdeaContent(
            IdeaTitle: "Top 5 Tools",
            Angle: "Productivity angle",
            Platforms: ["instagram", "linkedin"],
            Rationale: "High engagement on list content",
            BrandFitScore: 4.5);
        var gateway = new SucceedingGateway<ContentIdeaContent>(idea);
        var gen = new ContentIdeaGenerator(gateway, BeginContext("format:content-idea"));

        var result = await gen.GenerateAsync(BuildContext(), CancellationToken.None);

        Assert.Equal(FormatGenerationStatus.Succeeded, result.Status);
        Assert.Equal(idea.IdeaTitle, result.Payload!.IdeaTitle);
        Assert.Equal("content-idea.v2", result.PromptVersion);
    }

    [Fact]
    public void ContentIdeaGenerator_FormatKeyAndPromptVersion_AreCorrect()
    {
        var gen = new ContentIdeaGenerator(new SucceedingGateway<ContentIdeaContent>(BuildContentIdea()), BeginContext("format:content-idea"));
        Assert.Equal("content-idea", gen.FormatKey);
        Assert.Equal(ContentGenerationPromptVersions.ContentIdea, gen.PromptVersion);
    }

    [Fact]
    public async Task ContentIdeaGenerator_AiFailure_ReturnsFailedResult()
    {
        var gen = new ContentIdeaGenerator(new FailingGateway<ContentIdeaContent>(Error.Failure("fail", "fail")), BeginContext("format:content-idea"));
        var result = await gen.GenerateAsync(BuildContext(), CancellationToken.None);
        Assert.Equal(FormatGenerationStatus.Failed, result.Status);
    }

    // ================================================================
    // CTA VARIATION
    // ================================================================

    [Fact]
    public async Task CtaVariationGenerator_Success_ReturnsSucceededWithCtaVariationContent()
    {
        var cta = new CtaVariationContent(
            Variations: [
                new CtaVariation("Get started free", "friendly", "conversion"),
                new CtaVariation("Book a call", "professional", "lead-gen")
            ]);
        var gateway = new SucceedingGateway<CtaVariationContent>(cta);
        var gen = new CtaVariationGenerator(gateway, BeginContext("format:cta-variation"));

        var result = await gen.GenerateAsync(BuildContext(), CancellationToken.None);

        Assert.Equal(FormatGenerationStatus.Succeeded, result.Status);
        Assert.Equal(2, result.Payload!.Variations.Count);
        Assert.Equal("cta-variation.v2", result.PromptVersion);
    }

    [Fact]
    public void CtaVariationGenerator_FormatKeyAndPromptVersion_AreCorrect()
    {
        var gen = new CtaVariationGenerator(new SucceedingGateway<CtaVariationContent>(BuildCtaVariation()), BeginContext("format:cta-variation"));
        Assert.Equal("cta-variation", gen.FormatKey);
        Assert.Equal(ContentGenerationPromptVersions.CtaVariation, gen.PromptVersion);
    }

    [Fact]
    public async Task CtaVariationGenerator_AiFailure_ReturnsFailedResult()
    {
        var gen = new CtaVariationGenerator(new FailingGateway<CtaVariationContent>(Error.Failure("fail", "fail")), BeginContext("format:cta-variation"));
        var result = await gen.GenerateAsync(BuildContext(), CancellationToken.None);
        Assert.Equal(FormatGenerationStatus.Failed, result.Status);
    }

    // ================================================================
    // CAPTION
    // ================================================================

    [Fact]
    public async Task CaptionGenerator_Success_ReturnsSucceededWithCaptionContent()
    {
        var caption = new CaptionContent(
            PrimaryCaption: "Grow your business with these 5 tips",
            Alternates: ["5 tips every business owner needs"],
            Hashtags: ["#business", "#growth"],
            Mentions: null);
        var gateway = new SucceedingGateway<CaptionContent>(caption);
        var gen = new CaptionGenerator(gateway, BeginContext("format:caption"));

        var result = await gen.GenerateAsync(BuildContext(), CancellationToken.None);

        Assert.Equal(FormatGenerationStatus.Succeeded, result.Status);
        Assert.Equal(caption.PrimaryCaption, result.Payload!.PrimaryCaption);
        Assert.Equal("caption.v2", result.PromptVersion);
    }

    [Fact]
    public void CaptionGenerator_FormatKeyAndPromptVersion_AreCorrect()
    {
        var gen = new CaptionGenerator(new SucceedingGateway<CaptionContent>(BuildCaption()), BeginContext("format:caption"));
        Assert.Equal("caption", gen.FormatKey);
        Assert.Equal(ContentGenerationPromptVersions.Caption, gen.PromptVersion);
    }

    [Fact]
    public async Task CaptionGenerator_AiFailure_ReturnsFailedResult()
    {
        var gen = new CaptionGenerator(new FailingGateway<CaptionContent>(Error.Failure("fail", "fail")), BeginContext("format:caption"));
        var result = await gen.GenerateAsync(BuildContext(), CancellationToken.None);
        Assert.Equal(FormatGenerationStatus.Failed, result.Status);
    }

    // ================================================================
    // HOOK
    // ================================================================

    [Fact]
    public async Task HookGenerator_Success_ReturnsSucceededWithHookContent()
    {
        var hook = new HookContent(
            PrimaryHook: "Did you know 80% of businesses fail in year 1?",
            Alternates: ["Most businesses miss this one thing"],
            Tone: "provocative",
            Pattern: "fear-based");
        var gateway = new SucceedingGateway<HookContent>(hook);
        var gen = new HookGenerator(gateway, BeginContext("format:hook"));

        var result = await gen.GenerateAsync(BuildContext(), CancellationToken.None);

        Assert.Equal(FormatGenerationStatus.Succeeded, result.Status);
        Assert.Equal(hook.PrimaryHook, result.Payload!.PrimaryHook);
        Assert.Equal("hook.v2", result.PromptVersion);
    }

    [Fact]
    public void HookGenerator_FormatKeyAndPromptVersion_AreCorrect()
    {
        var gen = new HookGenerator(new SucceedingGateway<HookContent>(BuildHook()), BeginContext("format:hook"));
        Assert.Equal("hook", gen.FormatKey);
        Assert.Equal(ContentGenerationPromptVersions.Hook, gen.PromptVersion);
    }

    [Fact]
    public async Task HookGenerator_AiFailure_ReturnsFailedResult()
    {
        var gen = new HookGenerator(new FailingGateway<HookContent>(Error.Failure("fail", "fail")), BeginContext("format:hook"));
        var result = await gen.GenerateAsync(BuildContext(), CancellationToken.None);
        Assert.Equal(FormatGenerationStatus.Failed, result.Status);
    }

    // ================================================================
    // Helpers
    // ================================================================

    private static StoryContent BuildStory() =>
        new("Title", "Sub", "Body", [], "CTA", "Branded background", "@client", null);

    private static ReelScriptContent BuildReelScript() =>
        new("Hook", [new ReelScriptScene("0:00", "vo", "on-screen")], "Follow", null);

    private static ContentIdeaContent BuildContentIdea() =>
        new("Title", "Angle", ["instagram"], "Rationale", 4.0);

    private static CtaVariationContent BuildCtaVariation() =>
        new([new CtaVariation("text", "tone", "intent")]);

    private static CaptionContent BuildCaption() =>
        new("Primary", [], [], null);

    private static HookContent BuildHook() =>
        new("PrimaryHook", [], "tone", "pattern");

    private static GenerationContext BuildContext() => new(
        OrganizationId: OrgId,
        ClientId: ClientId,
        AnalysisId: AnalysisId,
        Analysis: BuildAnalysis(),
        BrandSnapshot: BrandSnapshot.FromBrandData(
            new BrandManualSnapshot(
                ["#FFF"],
                ["logo.png"],
                [],
                ["Roboto"],
                "professional",
                null),
            new StrategySnapshot(["education"], "SMBs", "3x/week"),
            null),
        CorrelationId: CorrelationId,
        AiProviderKey: "claude",
        AiModel: "claude-sonnet-4-6");

    private static ProviderCallContext BeginContext(string stepKey)
    {
        var ctx = new ProviderCallContext();
        ctx.Begin(OrgId, ClientId, "content-generation", stepKey, "v1", CorrelationId);
        return ctx;
    }

    private static SingleAnalyzerAnalysis BuildAnalysis() => new(
        Summary: "Strong content",
        DetectedHooks: ["hook1"],
        LikelyAudience: "SMBs",
        NarrativeStructure: "problem-solution",
        Strengths: ["clear CTA"],
        Weaknesses: ["low engagement"],
        DerivedIdeas: [new SingleAnalyzerIdea("Idea", "Concept", "Rationale",
            new SingleAnalyzerScore(3, 4, 3, 4, 4, 3))],
        ClientAdaptationRecommendations: ["Use brand voice"],
        Confidence: 0.85,
        Warnings: []);
}

// -------------------------------------------------------------------
// Test doubles (file-scoped)
// -------------------------------------------------------------------

file sealed class SucceedingGateway<TResponse> : IAiCompletionGateway
    where TResponse : notnull
{
    private readonly TResponse _payload;
    public SucceedingGateway(TResponse payload) => _payload = payload;
    public AiJsonCompletionRequest? LastRequest { get; private set; }

    public Task<Result<AiJsonCompletionResponse<T>>> CompleteJsonAsync<T>(
        AiJsonCompletionRequest request, CancellationToken ct)
        where T : notnull
    {
        LastRequest = request;
        var response = new AiJsonCompletionResponse<T>(
            (T)(object)_payload,
            "claude",
            "claude-sonnet-4-6",
            Array.Empty<Incoders.Hive.Domain.Integrations.ProviderFailureDiagnostic>());
        return Task.FromResult(Result.Success(response));
    }
}

file sealed class FailingGateway<TResponse> : IAiCompletionGateway
    where TResponse : notnull
{
    private readonly Error _error;
    public FailingGateway(Error error) => _error = error;

    public Task<Result<AiJsonCompletionResponse<T>>> CompleteJsonAsync<T>(
        AiJsonCompletionRequest request, CancellationToken ct)
        where T : notnull =>
        Task.FromResult(Result.Failure<AiJsonCompletionResponse<T>>(_error));
}
