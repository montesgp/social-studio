using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Formats;

/// <summary>
/// TDD tests for <see cref="CarouselGenerator"/>.
/// RED phase: references types that don't exist yet.
/// </summary>
public sealed class CarouselGeneratorTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid AnalysisId = Guid.NewGuid();
    private static readonly Guid CorrelationId = Guid.NewGuid();

    // -------------------------------------------------------------------
    // Task 2a: Success — brand-full + analysis-full produce carousel
    // -------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_FullBrandAndAnalysis_ReturnsSucceededWithCarouselContent()
    {
        var carousel = BuildValidCarousel();
        var gateway = new SucceedingGateway<CarouselContent>(carousel);
        var ctx = BuildContext();
        var providerCallContext = BeginContext(OrgId, ClientId, CorrelationId);

        var generator = new CarouselGenerator(gateway, providerCallContext);
        var result = await generator.GenerateAsync(ctx, CancellationToken.None);

        Assert.Equal(FormatGenerationStatus.Succeeded, result.Status);
        Assert.NotNull(result.Payload);
        Assert.Equal(carousel.CoverTitle, result.Payload.CoverTitle);
        // Latest: in-call post caption — mandatory publicationDescription + hashtags[].
        Assert.Equal("carousel.v8", result.PromptVersion);
        Assert.Empty(result.InferredFields);
    }

    // -------------------------------------------------------------------
    // Task 2b: Brand-missing — inferred fields populated in result
    // -------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_MissingBrandColors_InferredFieldsContainBrandPalette()
    {
        var carousel = BuildValidCarousel();
        var gateway = new SucceedingGateway<CarouselContent>(carousel);
        // Brand snapshot with no color palette → ColorPalette IsInferred=true
        var brand = BrandSnapshot.FromBrandData(
            brand: new BrandManualSnapshot(
                ColorPalette: Array.Empty<WeightedColor>(),  // empty → inferred
                Logos: ["logo.png"],
                BackgroundTemplates:
                [
                    new BrandBackgroundTemplateSnapshot(
                        "Story",
                        "/storage/client-assets/org/client/story.png",
                        "story")
                ],
                Typography: ["Roboto"],
                VoiceAndTone: "professional",
                Notes: null),
            strategy: null,
            monthlyObjective: null);
        var ctx = BuildContext(brand);
        var providerCallContext = BeginContext(OrgId, ClientId, CorrelationId);

        var generator = new CarouselGenerator(gateway, providerCallContext);
        var result = await generator.GenerateAsync(ctx, CancellationToken.None);

        Assert.Equal(FormatGenerationStatus.Succeeded, result.Status);
        Assert.Contains("brand.ColorPalette", result.InferredFields);
    }

    [Fact]
    public async Task GenerateAsync_FullClientContext_PromptIncludesBrandStrategyObjectivesAndAccounts()
    {
        var carousel = BuildValidCarousel();
        var gateway = new SucceedingGateway<CarouselContent>(carousel);
        var brand = BrandSnapshot.FromBrandData(
            brand: new BrandManualSnapshot(
                ColorPalette: [new WeightedColor("#123456", ColorRole.Primary, 0), new WeightedColor("#654321", ColorRole.Secondary, 1)],
                Logos: ["https://cdn.example.com/logo.png"],
                BackgroundTemplates:
                [
                    new BrandBackgroundTemplateSnapshot(
                        "Story",
                        "/storage/client-assets/org/client/story.png",
                        "story")
                ],
                Typography: ["Inter"],
                VoiceAndTone: "direct and useful",
                Notes: "Use clean product-led layouts."),
            strategy: new StrategySnapshot(
                ContentPillars: ["education", "product proof"],
                TargetAudience: "B2B founders",
                PostingFrequency: "4x/week"),
            monthlyObjective: new ClientMonthlyObjectiveSnapshot(
                Objectives: "Grow demo requests by 25%.",
                Kpis: ["MQL", "demo requests"]),
            client: new ClientProfileSnapshot(
                Name: "Northwind Labs",
                Description: "AI operations platform",
                SectorOrNiche: "B2B SaaS"),
            referentAccounts:
            [
                new ClientAccountSnapshot("linkedin", "LinkedIn", "https://linkedin.com/company/referent", "@referent", "Referent", "Editorial benchmark")
            ],
            competitorAccounts:
            [
                new ClientAccountSnapshot("linkedin", "LinkedIn", "https://linkedin.com/company/competitor", "@competitor", "Competitor", "Avoid similar claims")
            ]);
        var ctx = BuildContext(brand);
        var providerCallContext = BeginContext(OrgId, ClientId, CorrelationId);

        var generator = new CarouselGenerator(gateway, providerCallContext);
        await generator.GenerateAsync(ctx, CancellationToken.None);

        Assert.NotNull(gateway.LastRequest);
        var prompt = gateway.LastRequest!.InputJson;
        Assert.Contains("Northwind Labs", prompt);
        Assert.Contains("#123456", prompt);
        Assert.Contains("backgroundTemplates", prompt);
        Assert.Contains("/storage/client-assets/org/client/story.png", prompt);
        Assert.Contains("Inter", prompt);
        Assert.Contains("Grow demo requests by 25%.", prompt);
        Assert.Contains("MQL", prompt);
        Assert.Contains("@referent", prompt);
        Assert.Contains("@competitor", prompt);
        Assert.Contains("Educational Series", prompt);
        Assert.Contains("publicationDescription", prompt);
        // In-call post caption: the carousel call also asks for hashtags so the studio post carries them.
        Assert.Contains("hashtags", prompt);
    }

    [Fact]
    public async Task GenerateAsync_PromptIncludesClientDirectivePrecedenceAndBrandNotes()
    {
        var carousel = BuildValidCarousel();
        var gateway = new SucceedingGateway<CarouselContent>(carousel);
        var brand = BrandSnapshot.FromBrandData(
            brand: new BrandManualSnapshot(
                ColorPalette: [new WeightedColor("#123456", ColorRole.Primary, 0)],
                Logos: [],
                BackgroundTemplates: [],
                Typography: ["Inter"],
                VoiceAndTone: "direct",
                Notes: "Never sign the generated slides with the account handle."),
            strategy: null,
            monthlyObjective: null);
        var providerCallContext = BeginContext(OrgId, ClientId, CorrelationId);
        var generator = new CarouselGenerator(gateway, providerCallContext);

        await generator.GenerateAsync(BuildContext(brand), CancellationToken.None);

        Assert.NotNull(gateway.LastRequest);
        var prompt = gateway.LastRequest!.InputJson;
        // The authoritative client-directive clause is present so the agent treats notes as overrides.
        Assert.Contains("CLIENT DIRECTIVES", prompt);
        // The brand-manual note (an explicit prohibition) is threaded into the prompt verbatim.
        Assert.Contains("Never sign the generated slides with the account handle.", prompt);
    }

    [Fact]
    public async Task GenerateAsync_WithRegenerationObjection_IncludesUserCorrectionInPrompt()
    {
        var carousel = BuildValidCarousel();
        var gateway = new SucceedingGateway<CarouselContent>(carousel);
        var providerCallContext = BeginContext(OrgId, ClientId, CorrelationId);
        var generator = new CarouselGenerator(gateway, providerCallContext);

        await generator.GenerateAsync(
            BuildContext(regenerationObjection: "Adjust the hook to remove exaggerated claims."),
            CancellationToken.None);

        Assert.NotNull(gateway.LastRequest);
        Assert.Contains("userCorrection", gateway.LastRequest!.InputJson);
        Assert.Contains("Adjust the hook to remove exaggerated claims.", gateway.LastRequest.InputJson);
    }

    [Fact]
    public async Task GenerateAsync_PromptIncludesAttributionGuardrail()
    {
        var carousel = BuildValidCarousel();
        var gateway = new SucceedingGateway<CarouselContent>(carousel);
        var providerCallContext = BeginContext(OrgId, ClientId, CorrelationId);
        var generator = new CarouselGenerator(gateway, providerCallContext);

        await generator.GenerateAsync(BuildContext(), CancellationToken.None);

        Assert.NotNull(gateway.LastRequest);
        Assert.Contains("Source-origin identity", gateway.LastRequest!.InputJson);
        Assert.Contains("Final attribution must belong only to the target client/brand", gateway.LastRequest.InputJson);
    }

    // -------------------------------------------------------------------
    // Task 2c: AI provider failure → Status=failed with provider reason
    // -------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_AiProviderFails_ReturnsFailedWithProviderReason()
    {
        var providerError = Error.Failure("ai.unavailable", "AI provider unavailable");
        var gateway = new FailingGateway<CarouselContent>(providerError);
        var ctx = BuildContext();
        var providerCallContext = BeginContext(OrgId, ClientId, CorrelationId);

        var generator = new CarouselGenerator(gateway, providerCallContext);
        var result = await generator.GenerateAsync(ctx, CancellationToken.None);

        Assert.Equal(FormatGenerationStatus.Failed, result.Status);
        Assert.Null(result.Payload);
        Assert.Equal("provider-unavailable", result.FailureReason);
    }

    // -------------------------------------------------------------------
    // Task 2d: FormatKey and PromptVersion match expected constants
    // -------------------------------------------------------------------

    [Fact]
    public void CarouselGenerator_FormatKeyAndPromptVersion_AreCorrect()
    {
        var gateway = new SucceedingGateway<CarouselContent>(BuildValidCarousel());
        var ctx = BeginContext(OrgId, ClientId, CorrelationId);
        var generator = new CarouselGenerator(gateway, ctx);

        Assert.Equal("carousel", generator.FormatKey);
        Assert.Equal(ContentGenerationPromptVersions.Carousel, generator.PromptVersion);
    }

    // -------------------------------------------------------------------
    // Task 2e: GenerateBoxedAsync returns correctly boxed result
    // -------------------------------------------------------------------

    [Fact]
    public async Task GenerateBoxedAsync_Success_ReturnsBoxedSucceededResult()
    {
        var carousel = BuildValidCarousel();
        var gateway = new SucceedingGateway<CarouselContent>(carousel);
        var ctx = BuildContext();
        var providerCallContext = BeginContext(OrgId, ClientId, CorrelationId);

        var generator = new CarouselGenerator(gateway, providerCallContext);
        var boxed = await generator.GenerateBoxedAsync(ctx, CancellationToken.None);

        Assert.Equal(FormatGenerationStatus.Succeeded, boxed.Status);
        Assert.IsType<CarouselContent>(boxed.Payload);
    }

    // -------------------------------------------------------------------
    // Real-generation fix (2026-08-27): the imagePromptHint instruction told the copywriter to
    // "reference the client's explicit palette, typography, logos, and visual notes" — which the
    // model reasonably read as "describe literal text/typography AS the image's visual content."
    // Confirmed root cause of a real generation where the image model baked unreadable, unremovable
    // text into a slide's background pixels (fighting the negative prompt's own "no text" terms).
    // The instruction must still let the copywriter reference the client's visual mood (palette,
    // decorative motifs, photographic style) without ever asking for literal on-image text/typography
    // content — that layer is rendered separately, deterministically, and is the only editable one.
    // -------------------------------------------------------------------

    [Fact]
    public async Task BuildPromptJson_Instruction_NeverAsksImagePromptHintToDescribeOnImageText()
    {
        var carousel = BuildValidCarousel();
        var gateway = new SucceedingGateway<CarouselContent>(carousel);
        var ctx = BuildContext();
        var providerCallContext = BeginContext(OrgId, ClientId, CorrelationId);

        var generator = new CarouselGenerator(gateway, providerCallContext);
        await generator.GenerateAsync(ctx, CancellationToken.None);

        var instruction = ExtractInstruction(gateway.LastRequest!.InputJson);

        // The old wording literally told the copywriter to "reference... typography" — which reads
        // as "put text in the picture." It must be gone.
        Assert.DoesNotContain(
            "Image prompt hints must reference the client's explicit palette, typography, logos",
            instruction,
            StringComparison.Ordinal);

        // The replacement must explicitly forbid describing on-image text/typography as content —
        // not just omit the old phrase by accident.
        Assert.Contains("imagePromptHint", instruction, StringComparison.Ordinal);
        Assert.Contains("never describe literal text", instruction, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractInstruction(string inputJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(inputJson);
        return doc.RootElement.GetProperty("instruction").GetString()!;
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static CarouselContent BuildValidCarousel() => new(
        CoverTitle: "5 Ways to Grow Your Brand",
        Slides: [
            new CarouselSlide("Slide 1", "Body text", "bright colors"),
            new CarouselSlide("Slide 2", "Body text 2", "minimalist")
        ],
        CtaText: "Follow us for more",
        CtaLink: null);

    private static GenerationContext BuildContext(
        BrandSnapshot? brand = null,
        string? regenerationObjection = null) => new(
        OrganizationId: OrgId,
        ClientId: ClientId,
        AnalysisId: AnalysisId,
        Analysis: BuildAnalysis(),
        BrandSnapshot: brand ?? BrandSnapshot.FromBrandData(
            brand: new BrandManualSnapshot(
                ColorPalette: [new WeightedColor("#FFF", ColorRole.Primary, 0), new WeightedColor("#000", ColorRole.Neutral, 1)],
                Logos: ["logo.png"],
                BackgroundTemplates:
                [
                    new BrandBackgroundTemplateSnapshot(
                        "Story",
                        "/storage/client-assets/org/client/story.png",
                        "story")
                ],
                Typography: ["Roboto"],
                VoiceAndTone: "professional",
                Notes: null),
            strategy: new StrategySnapshot(
                ContentPillars: ["education", "trust"],
                TargetAudience: "SMB owners",
                PostingFrequency: "3x/week"),
            monthlyObjective: new ClientMonthlyObjectiveSnapshot(
                Objectives: "Increase qualified leads.",
                Kpis: ["qualified leads"]),
            client: new ClientProfileSnapshot(
                Name: "Acme",
                Description: "B2B services",
                SectorOrNiche: "SMB operations"),
            referentAccounts:
            [
                new ClientAccountSnapshot("linkedin", "LinkedIn", "https://linkedin.com/company/referent", "@referent", "Referent", "Benchmark")
            ],
            competitorAccounts:
            [
                new ClientAccountSnapshot("linkedin", "LinkedIn", "https://linkedin.com/company/competitor", "@competitor", "Competitor", "Avoid claims")
            ],
            contentStrategy: new ContentStrategySnapshot(
                StrategyText: "Focus on educational carousels this period.",
                Source: 1,
                Status: 2),
            calendar: new CalendarSnapshot(Slots:
            [
                new CalendarSlotSnapshot(Day: 10, ContentFormat: "carousel", SortOrder: 0),
            ]),
            period: new PeriodSnapshot(Year: 2026, Month: 6, PeriodKind: 0)),
        CorrelationId: CorrelationId,
        AiProviderKey: "claude",
        AiModel: "claude-sonnet-4-6",
        RegenerationObjection: regenerationObjection);

    private static ProviderCallContext BeginContext(Guid orgId, Guid? clientId, Guid correlationId)
    {
        var ctx = new ProviderCallContext();
        ctx.Begin(orgId, clientId, "content-generation", "format:carousel", "carousel.v3", correlationId);
        return ctx;
    }

    private static SingleAnalyzerAnalysis BuildAnalysis() => new(
        Summary: "Strong educational content with clear CTAs",
        DetectedHooks: ["question-hook", "stats-hook"],
        LikelyAudience: "SMB owners 30-50",
        NarrativeStructure: "problem-solution",
        Strengths: ["clear value prop", "strong CTA"],
        Weaknesses: ["low visual variety"],
        DerivedIdeas: [new SingleAnalyzerIdea(
            "Educational Series",
            "Weekly tips carousel",
            "Proven format for the niche",
            new SingleAnalyzerScore(4, 5, 4, 4, 5, 4))],
        ClientAdaptationRecommendations: ["Use brand colors consistently"],
        Confidence: 0.9,
        Warnings: [],
        Patterns: PatternSet.Empty);
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
