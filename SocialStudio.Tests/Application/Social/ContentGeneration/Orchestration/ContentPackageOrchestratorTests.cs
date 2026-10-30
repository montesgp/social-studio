using SocialStudio.Application.Abstractions.ContentAnalysis;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Observability;
using SocialStudio.Domain.Integrations;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Orchestration;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentGeneration;
using SocialStudio.Domain.Social.SingleAnalyzer;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Orchestration;

/// <summary>
/// TDD tests for <see cref="ContentPackageOrchestrator"/>.
/// RED phase: references types that don't exist yet.
/// </summary>
public sealed class ContentPackageOrchestratorTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid AnalysisId = Guid.NewGuid();
    private static readonly Guid CorrelationId = Guid.NewGuid();

    // -------------------------------------------------------------------
    // All formats requested + all succeed → OverallStatus = succeeded
    // -------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_AllFormatsSucceed_ReturnsSucceededWithAllResults()
    {
        var generators = new IFormatContentGenerator[]
        {
            new FakeGenerator("carousel", FormatGenerationStatus.Succeeded),
            new FakeGenerator("story", FormatGenerationStatus.Succeeded),
        };
        var brandLoader = new SucceedingBrandLoader();
        var orchestrator = new ContentPackageOrchestrator(generators, brandLoader, new ProviderCallContext(), new SucceedingWorkflowResolver());

        var request = new OrchestratorRequest(
            OrgId, ClientId, AnalysisId, BuildAnalysis(),
            ["carousel", "story"], CorrelationId);

        var result = await orchestrator.RunAsync(request, CancellationToken.None);

        Assert.Equal(OrchestratorStatus.Succeeded, result.OverallStatus);
        Assert.Equal(2, result.PerFormatResults.Count);
        Assert.True(result.PerFormatResults.ContainsKey("carousel"));
        Assert.True(result.PerFormatResults.ContainsKey("story"));
        Assert.Equal(FormatGenerationStatus.Succeeded, result.PerFormatResults["carousel"].Status);
        Assert.Equal(FormatGenerationStatus.Succeeded, result.PerFormatResults["story"].Status);
    }

    // -------------------------------------------------------------------
    // Subset requested → only those formats are executed
    // -------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_SubsetRequested_OnlyRequestedFormatsExecuted()
    {
        var generators = new IFormatContentGenerator[]
        {
            new FakeGenerator("carousel", FormatGenerationStatus.Succeeded),
            new FakeGenerator("story", FormatGenerationStatus.Succeeded),
            new FakeGenerator("hook", FormatGenerationStatus.Succeeded),
        };
        var brandLoader = new SucceedingBrandLoader();
        var orchestrator = new ContentPackageOrchestrator(generators, brandLoader, new ProviderCallContext(), new SucceedingWorkflowResolver());

        var request = new OrchestratorRequest(
            OrgId, ClientId, AnalysisId, BuildAnalysis(),
            ["carousel"], CorrelationId);

        var result = await orchestrator.RunAsync(request, CancellationToken.None);

        Assert.Equal(OrchestratorStatus.Succeeded, result.OverallStatus);
        Assert.Single(result.PerFormatResults);
        Assert.True(result.PerFormatResults.ContainsKey("carousel"));
        Assert.False(result.PerFormatResults.ContainsKey("story"));
    }

    // -------------------------------------------------------------------
    // One format fails → others complete; OverallStatus = partial
    // -------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_OneFormatFails_RemainingComplete_ReturnsPartial()
    {
        var generators = new IFormatContentGenerator[]
        {
            new FakeGenerator("carousel", FormatGenerationStatus.Succeeded),
            new FakeGenerator("story", FormatGenerationStatus.Failed),
        };
        var brandLoader = new SucceedingBrandLoader();
        var orchestrator = new ContentPackageOrchestrator(generators, brandLoader, new ProviderCallContext(), new SucceedingWorkflowResolver());

        var request = new OrchestratorRequest(
            OrgId, ClientId, AnalysisId, BuildAnalysis(),
            ["carousel", "story"], CorrelationId);

        var result = await orchestrator.RunAsync(request, CancellationToken.None);

        Assert.Equal(OrchestratorStatus.Partial, result.OverallStatus);
        Assert.Equal(FormatGenerationStatus.Succeeded, result.PerFormatResults["carousel"].Status);
        Assert.Equal(FormatGenerationStatus.Failed, result.PerFormatResults["story"].Status);
        Assert.NotNull(result.PerFormatResults["story"].FailureReason);
    }

    // -------------------------------------------------------------------
    // All formats fail → OverallStatus = failed
    // -------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_AllFormatsFail_ReturnsOverallFailed()
    {
        var generators = new IFormatContentGenerator[]
        {
            new FakeGenerator("carousel", FormatGenerationStatus.Failed),
            new FakeGenerator("story", FormatGenerationStatus.Failed),
        };
        var brandLoader = new SucceedingBrandLoader();
        var orchestrator = new ContentPackageOrchestrator(generators, brandLoader, new ProviderCallContext(), new SucceedingWorkflowResolver());

        var request = new OrchestratorRequest(
            OrgId, ClientId, AnalysisId, BuildAnalysis(),
            ["carousel", "story"], CorrelationId);

        var result = await orchestrator.RunAsync(request, CancellationToken.None);

        Assert.Equal(OrchestratorStatus.Failed, result.OverallStatus);
        Assert.All(result.PerFormatResults.Values, r => Assert.Equal(FormatGenerationStatus.Failed, r.Status));
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static SingleAnalyzerAnalysis BuildAnalysis() => new(
        Summary: "Test summary",
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

file sealed class FakeGenerator : IFormatContentGenerator
{
    private readonly FormatGenerationStatus _status;

    public FakeGenerator(string formatKey, FormatGenerationStatus status)
    {
        FormatKey = formatKey;
        _status = status;
    }

    public string FormatKey { get; }
    public string PromptVersion => "fake.v1";

    public Task<FormatGenerationResult<object>> GenerateBoxedAsync(GenerationContext ctx, CancellationToken ct)
    {
        FormatGenerationResult<object> result = _status == FormatGenerationStatus.Succeeded
            ? FormatGenerationResult<object>.Succeeded(new object(), PromptVersion, [])
            : FormatGenerationResult<object>.Failed(PromptVersion, "fake-failure");

        return Task.FromResult(result);
    }
}

file sealed class SucceedingBrandLoader : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid organizationId, Guid clientId, CancellationToken ct = default) =>
        Task.FromResult(BrandSnapshot.Empty);
}

file sealed class SucceedingWorkflowResolver : IWorkflowConfigurationResolver
{
    public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
        WorkflowStepResolutionRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
        [
            new WorkflowStepRouteCandidate(Guid.NewGuid(), "claude", Guid.NewGuid(),
                IntegrationConnectionMode.BringYourOwnKey, 1, "claude-sonnet-4-6", null, true)
        ]));
}
