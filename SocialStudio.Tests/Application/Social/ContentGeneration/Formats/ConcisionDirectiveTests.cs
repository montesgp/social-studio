using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Observability;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Carousel;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.FeedPost;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Story;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentGeneration;
using SocialStudio.Domain.Social.SingleAnalyzer;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Formats;

/// <summary>
/// WU-7 TDD tests — concision directive injection (CG-CON-01, CG-CON-02).
///
/// These tests assert that the three format generators include the shared
/// ConcisionConstraint in their prompt JSON. They are written RED-first
/// (the constant and injection do not exist yet) and turned GREEN by
/// adding GenerationPromptContext.ConcisionConstraint and appending it in
/// each generator's BuildPromptJson.
/// </summary>
public sealed class ConcisionDirectiveTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid CorrelationId = Guid.NewGuid();

    // ─── CarouselGenerator ───────────────────────────────────────────────────

    /// <summary>
    /// CG-CON-01: The carousel prompt must state the one-concept-across-slides directive.
    /// The ConcisionConstraint text ("carousels = ONE concept") must appear in the assembled
    /// prompt JSON, verified against the serialised instruction field.
    /// </summary>
    [Fact]
    public async Task CarouselGenerator_Prompt_ContainsConcisionConstraint()
    {
        var gateway = new CapturingGateway<CarouselContent>(BuildValidCarousel());
        var ctx = BuildProviderCallContext();
        var generator = new CarouselGenerator(gateway, ctx);

        await generator.GenerateAsync(BuildContext(), CancellationToken.None);

        Assert.NotNull(gateway.LastRequest);
        var prompt = gateway.LastRequest!.InputJson;
        Assert.Contains(GenerationPromptContext.ConcisionConstraint, prompt);
    }

    /// <summary>
    /// CG-CON-01 (carousel): the carousel prompt must also indicate
    /// per-slot length guidance (one-concept framing), distinct from the single-format framing.
    /// </summary>
    [Fact]
    public async Task CarouselGenerator_Prompt_ContainsOneSlideConcept()
    {
        var gateway = new CapturingGateway<CarouselContent>(BuildValidCarousel());
        var generator = new CarouselGenerator(gateway, BuildProviderCallContext());

        await generator.GenerateAsync(BuildContext(), CancellationToken.None);

        var prompt = gateway.LastRequest!.InputJson;
        // The concision constraint mentions "ONE concept" for carousels — verify key phrase
        Assert.Contains("ONE concept", prompt);
    }

    // ─── StoryGenerator ──────────────────────────────────────────────────────

    /// <summary>
    /// CG-CON-02: The story prompt must include the super-summary directive
    /// (ONE super-summary idea). ConcisionConstraint carries the shared text.
    /// </summary>
    [Fact]
    public async Task StoryGenerator_Prompt_ContainsConcisionConstraint()
    {
        var gateway = new CapturingGateway<StoryContent>(BuildValidStory());
        var generator = new StoryGenerator(gateway, BuildProviderCallContext());

        await generator.GenerateAsync(BuildContext(), CancellationToken.None);

        Assert.NotNull(gateway.LastRequest);
        var prompt = gateway.LastRequest!.InputJson;
        Assert.Contains(GenerationPromptContext.ConcisionConstraint, prompt);
    }

    /// <summary>
    /// CG-CON-02 (story): the story prompt must mention the super-summary framing
    /// for singles (story, feed = ONE super-summary idea).
    /// </summary>
    [Fact]
    public async Task StoryGenerator_Prompt_ContainsSuperSummaryFraming()
    {
        var gateway = new CapturingGateway<StoryContent>(BuildValidStory());
        var generator = new StoryGenerator(gateway, BuildProviderCallContext());

        await generator.GenerateAsync(BuildContext(), CancellationToken.None);

        var prompt = gateway.LastRequest!.InputJson;
        Assert.Contains("super-summary", prompt);
    }

    // ─── FeedPostGenerator ───────────────────────────────────────────────────

    /// <summary>
    /// CG-CON-02: The feed-post prompt must include the super-summary directive.
    /// ConcisionConstraint carries the shared text.
    /// </summary>
    [Fact]
    public async Task FeedPostGenerator_Prompt_ContainsConcisionConstraint()
    {
        var gateway = new CapturingGateway<FeedPostContent>(BuildValidFeedPost());
        var generator = new FeedPostGenerator(gateway, BuildProviderCallContext());

        await generator.GenerateAsync(BuildContext(), CancellationToken.None);

        Assert.NotNull(gateway.LastRequest);
        var prompt = gateway.LastRequest!.InputJson;
        Assert.Contains(GenerationPromptContext.ConcisionConstraint, prompt);
    }

    [Fact]
    public async Task FeedPostGenerator_Prompt_ContainsSuperSummaryFraming()
    {
        var gateway = new CapturingGateway<FeedPostContent>(BuildValidFeedPost());
        var generator = new FeedPostGenerator(gateway, BuildProviderCallContext());

        await generator.GenerateAsync(BuildContext(), CancellationToken.None);

        var prompt = gateway.LastRequest!.InputJson;
        Assert.Contains("super-summary", prompt);
    }

    // ─── Shared ConcisionConstraint constant ─────────────────────────────────

    /// <summary>
    /// The constant must exist on GenerationPromptContext and be non-empty.
    /// </summary>
    [Fact]
    public void ConcisionConstraint_Constant_IsNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(GenerationPromptContext.ConcisionConstraint));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    // ─── client-style-fidelity CSF-CG-06: keyword highlight ──────────────────

    [Fact]
    public async Task CarouselGenerator_ClientObservedHighlighting_AsksForTitleHighlights()
    {
        var gateway = new CapturingGateway<CarouselContent>(BuildValidCarousel());
        var generator = new CarouselGenerator(gateway, BuildProviderCallContext());
        var profile = new ClientAestheticProfile([], null, null, null,
            ColorPairings: [new ColorPairing("#0E0E0E", "#FFFFFF", 1, "#A6CE3C", SlideCount: 10, Kind: ColorPairingKinds.Flat)]);

        await generator.GenerateAsync(BuildContext() with { AestheticProfile = profile }, CancellationToken.None);

        var prompt = gateway.LastRequest!.InputJson;
        Assert.Contains("HIGHLIGHT RULE", prompt);
        Assert.Contains("coverTitleHighlight", prompt);
        Assert.Contains("titleHighlight", prompt);
    }

    [Fact]
    public async Task CarouselGenerator_DerivedAccentOnly_DoesNotAskForHighlights()
    {
        // An accent derived from the palette is not evidence the client highlights words.
        var gateway = new CapturingGateway<CarouselContent>(BuildValidCarousel());
        var generator = new CarouselGenerator(gateway, BuildProviderCallContext());
        var profile = new ClientAestheticProfile([], null, null, null,
            ColorPairings: [new ColorPairing("#0E0E0E", "#FFFFFF", 1, "#A6CE3C")]);

        await generator.GenerateAsync(BuildContext() with { AestheticProfile = profile }, CancellationToken.None);

        var prompt = gateway.LastRequest!.InputJson;
        Assert.DoesNotContain("HIGHLIGHT RULE", prompt);
        Assert.DoesNotContain("titleHighlight", prompt);
    }

    private static CarouselContent BuildValidCarousel() => new(
        CoverTitle: "Grow Your Brand",
        Slides: [new CarouselSlide("Slide 1", "Body", null)],
        CtaText: "Follow us",
        CtaLink: null);

    private static StoryContent BuildValidStory() => new(
        Title: "A bold headline",
        Subtitle: null,
        Body: null,
        Callouts: [],
        CtaText: "Swipe up",
        BackgroundPrompt: null,
        SignatureText: null,
        LayoutHint: null);

    private static FeedPostContent BuildValidFeedPost() => new(
        Title: "A bold headline",
        Subtitle: null,
        Body: null,
        Callouts: [],
        CtaText: "Learn more",
        BackgroundPrompt: null,
        SignatureText: null,
        LayoutHint: null);

    private static ProviderCallContext BuildProviderCallContext()
    {
        var ctx = new ProviderCallContext();
        ctx.Begin(OrgId, ClientId, "content-generation", "format:test", "test.v1", CorrelationId);
        return ctx;
    }

    private static GenerationContext BuildContext() => new(
        OrganizationId: OrgId,
        ClientId: ClientId,
        AnalysisId: Guid.NewGuid(),
        Analysis: new SingleAnalyzerAnalysis(
            Summary: "Good content",
            DetectedHooks: [],
            LikelyAudience: "SMB",
            NarrativeStructure: "problem-solution",
            Strengths: [],
            Weaknesses: [],
            DerivedIdeas: [new SingleAnalyzerIdea(
                "Idea", "Concept", "Rationale",
                new SingleAnalyzerScore(4, 4, 4, 4, 4, 4))],
            ClientAdaptationRecommendations: [],
            Confidence: 0.9,
            Warnings: [],
            Patterns: PatternSet.Empty),
        BrandSnapshot: BrandSnapshot.FromBrandData(null, null, null),
        CorrelationId: CorrelationId,
        AiProviderKey: "claude",
        AiModel: "claude-sonnet-4-6");
}

// ─── Test doubles ────────────────────────────────────────────────────────────

file sealed class CapturingGateway<TPayload> : IAiCompletionGateway
    where TPayload : notnull
{
    private readonly TPayload _payload;
    public AiJsonCompletionRequest? LastRequest { get; private set; }

    public CapturingGateway(TPayload payload) => _payload = payload;

    public Task<Result<AiJsonCompletionResponse<T>>> CompleteJsonAsync<T>(
        AiJsonCompletionRequest request,
        CancellationToken ct)
        where T : notnull
    {
        LastRequest = request;
        var response = new AiJsonCompletionResponse<T>(
            (T)(object)_payload,
            "claude",
            "claude-sonnet-4-6",
            Array.Empty<SocialStudio.Domain.Integrations.ProviderFailureDiagnostic>());
        return Task.FromResult(Result.Success(response));
    }
}
