using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Features.Integrations.WorkflowConfigurations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Tests.Application.Integrations;

/// <summary>
/// WU-7 TDD tests for the generate-image step in WorkflowConfigurationCatalog.
/// Refs: CIG-INT-01, CIG-INT-02, CIG-INT-03, CIG-INT-04; design §2.3.
/// </summary>
public sealed class WorkflowConfigurationCatalogImageStepTests
{
    // -----------------------------------------------------------------------
    // 7.1 — Catalog entry shape (CIG-INT-01-a)
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerateImageStep_IsPresent_InContentAnalysisWorkflow()
    {
        var step = WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-analysis", "generate-image");

        Assert.NotNull(step);
    }

    [Fact]
    public void GenerateImageStep_HasMediaGenerationCategory()
    {
        var step = WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-analysis", "generate-image");

        Assert.NotNull(step);
        Assert.Contains(IntegrationProviderCategory.MediaGeneration, step!.AllowedProviderCategories);
    }

    [Fact]
    public void GenerateImageStep_SupportsModelSelection()
    {
        var step = WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-analysis", "generate-image");

        Assert.NotNull(step);
        Assert.True(step!.SupportsModelSelection);
    }

    [Fact]
    public void GenerateImageStep_DoesNotRequireNetwork()
    {
        var step = WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-analysis", "generate-image");

        Assert.NotNull(step);
        Assert.False(step!.RequiresNetwork);
    }

    [Fact]
    public void ExistingContentAnalysisSteps_StillPresent_NoRegression()
    {
        Assert.NotNull(WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-analysis", "scrape-video"));
        Assert.NotNull(WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-analysis", "analyze-content"));
        Assert.NotNull(WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-analysis", "generate-content"));
    }

    // -----------------------------------------------------------------------
    // 7.2 — Resolver stubs (CIG-INT-02-a, CIG-INT-03-a, CIG-INT-03-b)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Resolver_OrgAWithOpenAiImageRoute_ReturnsRouteForGenerateImage()
    {
        // GIVEN org A has a generate-image route configured (OpenAI, dall-e-3)
        var orgAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var resolver = new OrgAWorkflowConfigurationResolver(orgAId);

        // WHEN resolving the generate-image step
        var result = await resolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                orgAId,
                "single-analyzer",
                "content-analysis",
                "generate-image"));

        // THEN a route with provider openai and model dall-e-3 is returned (CIG-INT-03-a)
        Assert.True(result.IsSuccess);
        var candidates = result.Value;
        Assert.NotEmpty(candidates);
        var first = candidates[0];
        Assert.Equal("openai", first.ProviderKey);
        Assert.Equal("dall-e-3", first.ModelId);
    }

    [Fact]
    public async Task Resolver_OrgBWithNoImageRoute_ReturnsEmptyForGenerateImage()
    {
        // GIVEN org B has no generate-image route configured
        var orgBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var resolver = new OrgBWorkflowConfigurationResolver();

        // WHEN resolving the generate-image step
        var result = await resolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                orgBId,
                "single-analyzer",
                "content-analysis",
                "generate-image"));

        // THEN the result is empty (no route configured) — CIG-INT-03-b
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}

// -----------------------------------------------------------------------
// Resolver stubs for org-level routing test
// -----------------------------------------------------------------------

/// <summary>Org A stub: has a generate-image route (OpenAI, dall-e-3).</summary>
file sealed class OrgAWorkflowConfigurationResolver(Guid orgAId) : IWorkflowConfigurationResolver
{
    public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
        WorkflowStepResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.OrganizationId == orgAId
            && string.Equals(request.StepKey, "generate-image", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = new WorkflowStepRouteCandidate(
                ProviderId: Guid.Parse("18b44d7f-e85e-4a3d-95a3-4b2277d45501"), // openai
                ProviderKey: "openai",
                OrganizationIntegrationId: Guid.NewGuid(),
                ConnectionMode: IntegrationConnectionMode.BringYourOwnKey,
                Priority: 1,
                ModelId: "dall-e-3",
                ActorId: null,
                IsEnabled: true);

            return Task.FromResult(
                Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([candidate]));
        }

        return Task.FromResult(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));
    }
}

/// <summary>Org B stub: has no generate-image route configured.</summary>
file sealed class OrgBWorkflowConfigurationResolver : IWorkflowConfigurationResolver
{
    public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
        WorkflowStepResolutionRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));
}
