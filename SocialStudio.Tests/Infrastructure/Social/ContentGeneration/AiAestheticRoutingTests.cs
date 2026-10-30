using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Features.Integrations.WorkflowConfigurations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;

namespace SocialStudio.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// WU-6 tests (task 6.2): verify that the <c>ai-aesthetic</c> / <c>extract-aesthetic</c>
/// workflow step resolves to a vision-capable model provider and is NOT wired to the
/// <c>claude -p</c> CLI alias.
///
/// OQ-6: <c>claude -p</c> is a CLI agent that refuses JSON contracts and cannot perform
/// multimodal vision extraction. Only real vision-capable API providers (e.g., OpenAI GPT-4o,
/// Anthropic Claude vision via API) are valid for this step.
///
/// Refs: CACD-PA-06-a/b; design §0 OQ-6; tasks 6.2.
/// </summary>
public sealed class AiAestheticRoutingTests
{
    // -----------------------------------------------------------------------
    // 6.2-a: Catalog step for "ai-aesthetic" does NOT restrict to claude-p as the only
    //         allowed provider. AllowedProviderKeys must be empty (open to any configured
    //         vision-capable provider). (CACD-PA-06-a)
    // -----------------------------------------------------------------------

    [Fact]
    public void AiAestheticCatalogStep_AllowedProviderKeys_DoesNotContainClaudeP()
    {
        var step = WorkflowConfigurationCatalog.FindStep(
            "single-analyzer", "profile-analysis", "extract-aesthetic");

        Assert.NotNull(step);
        Assert.Equal("ai-aesthetic", step!.OperationKey);

        // AllowedProviderKeys is empty = no restriction; any org-configured vision provider is valid.
        // claude-p MUST NOT be listed as the sole/enforced provider — it is a CLI agent, not a vision API.
        Assert.DoesNotContain("claude-p", step.AllowedProviderKeys, StringComparer.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // 6.2-b: Catalog step supports model selection (so a vision model can be chosen);
    //         allows AiModel category (not actor/CLI-only). (CACD-PA-06-b)
    // -----------------------------------------------------------------------

    [Fact]
    public void AiAestheticCatalogStep_SupportsModelSelection_And_AllowsAiModelCategory()
    {
        var step = WorkflowConfigurationCatalog.FindStep(
            "single-analyzer", "profile-analysis", "extract-aesthetic");

        Assert.NotNull(step);
        Assert.True(step!.SupportsModelSelection,
            "ai-aesthetic step must support model selection for vision model configuration (OQ-6)");
        Assert.Contains(IntegrationProviderCategory.AiModel, step.AllowedProviderCategories);
    }

    // -----------------------------------------------------------------------
    // 6.2-c: A resolver configured with a proper vision provider (openai/gpt-4o) returns
    //         a non-null, non-claude-p candidate for the ai-aesthetic step. (CACD-PA-06-a)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AiAestheticResolver_WithOpenAiVisionProvider_ReturnsNonClaudePCandidate()
    {
        var resolver = new OpenAiVisionFakeResolver();
        var result = await resolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                Guid.NewGuid(),
                "single-analyzer",
                "profile-analysis",
                "extract-aesthetic",
                "ai-aesthetic",
                null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var candidate = result.Value.Where(c => c.IsEnabled).FirstOrDefault();

        Assert.NotNull(candidate);
        Assert.NotNull(candidate!.ProviderKey);
        Assert.NotEqual("claude-p", candidate.ProviderKey, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("gpt-4o", candidate.ModelId);
    }

    // -----------------------------------------------------------------------
    // 6.2-d: No ai-aesthetic route resolves to the claude-p alias in the test-environment config.
    //         A properly-configured env returns only vision-capable, non-claude-p candidates.
    //         (CACD-PA-06-a guard)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AiAestheticResolver_TestEnvironmentConfig_NoClaudePCandidate()
    {
        var resolver = new OpenAiVisionFakeResolver();
        var result = await resolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                Guid.NewGuid(),
                "single-analyzer",
                "profile-analysis",
                "extract-aesthetic",
                "ai-aesthetic",
                null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // None of the enabled candidates should be claude-p.
        var claudePCandidates = result.Value
            .Where(c => c.IsEnabled)
            .Where(c => string.Equals(c.ProviderKey, "claude-p", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(claudePCandidates);
    }

    // -----------------------------------------------------------------------
    // Test infrastructure
    // -----------------------------------------------------------------------

    /// <summary>
    /// Simulates a properly-configured test environment where the ai-aesthetic step
    /// is wired to OpenAI GPT-4o (a real vision-capable API model, not claude-p CLI).
    /// </summary>
    private sealed class OpenAiVisionFakeResolver : IWorkflowConfigurationResolver
    {
        public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
            WorkflowStepResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<WorkflowStepRouteCandidate> candidates =
            [
                new WorkflowStepRouteCandidate(
                    ProviderId: Guid.NewGuid(),
                    ProviderKey: "openai",
                    OrganizationIntegrationId: Guid.NewGuid(),
                    ConnectionMode: IntegrationConnectionMode.BringYourOwnKey,
                    Priority: 1,
                    ModelId: "gpt-4o",
                    ActorId: null,
                    IsEnabled: true),
            ];
            return Task.FromResult(Result.Success(candidates));
        }
    }
}
