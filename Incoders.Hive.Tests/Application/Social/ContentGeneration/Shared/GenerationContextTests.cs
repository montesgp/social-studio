using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// Unit tests for GenerationContext value object.
/// RED phase: these tests reference types that do not exist yet.
/// </summary>
public sealed class GenerationContextTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid AnalysisId = Guid.NewGuid();
    private static readonly Guid CorrelationId = Guid.NewGuid();

    [Fact]
    public void GenerationContext_WithAllFields_ExposesCorrectValues()
    {
        var analysis = BuildAnalysis();
        var brand = BrandSnapshot.Empty;

        var ctx = new GenerationContext(
            OrganizationId: OrgId,
            ClientId: ClientId,
            AnalysisId: AnalysisId,
            Analysis: analysis,
            BrandSnapshot: brand,
            CorrelationId: CorrelationId,
            AiProviderKey: "claude",
            AiModel: "claude-sonnet-4-6");

        Assert.Equal(OrgId, ctx.OrganizationId);
        Assert.Equal(ClientId, ctx.ClientId);
        Assert.Equal(AnalysisId, ctx.AnalysisId);
        Assert.Same(analysis, ctx.Analysis);
        Assert.Same(brand, ctx.BrandSnapshot);
        Assert.Equal(CorrelationId, ctx.CorrelationId);
        Assert.Null(ctx.RegenerationObjection);
    }

    [Fact]
    public void GenerationContext_NullClientId_IsAllowed()
    {
        var ctx = new GenerationContext(
            OrganizationId: OrgId,
            ClientId: null,
            AnalysisId: AnalysisId,
            Analysis: BuildAnalysis(),
            BrandSnapshot: BrandSnapshot.Empty,
            CorrelationId: CorrelationId,
            AiProviderKey: "claude",
            AiModel: "claude-sonnet-4-6");

        Assert.Null(ctx.ClientId);
    }

    // helpers
    private static SingleAnalyzerAnalysis BuildAnalysis() => new(
        Summary: "Test summary",
        DetectedHooks: ["hook1"],
        LikelyAudience: "audience",
        NarrativeStructure: "structure",
        Strengths: ["strength"],
        Weaknesses: ["weakness"],
        DerivedIdeas: [new SingleAnalyzerIdea("title", "concept", "rationale",
            new SingleAnalyzerScore(3, 3, 3, 3, 3, 3))],
        ClientAdaptationRecommendations: ["rec1"],
        Confidence: 0.8,
        Warnings: []);
}
