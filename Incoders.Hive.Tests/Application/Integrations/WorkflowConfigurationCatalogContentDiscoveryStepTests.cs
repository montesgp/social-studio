using Incoders.Hive.Application.Features.Integrations.WorkflowConfigurations;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Tests.Application.Integrations;

/// <summary>
/// WU-6 tests for the <c>content-discovery</c> analysis type and its <c>discover-niche-content</c>
/// step, placed under the EXISTING <c>single-analyzer</c> workflow key (corrected placement — see
/// design D11 amendment): once the operator-selected candidate becomes a
/// <c>SingleContentAnalysis</c>, the downstream pipeline (AI analysis, generation, optional imagery)
/// is IDENTICAL to the by-URL <c>content-analysis</c> flow, so it reuses the SAME configuration
/// rather than duplicating the AI steps. Refs: INT-DISC-02, INT-DISC-03.
/// </summary>
public sealed class WorkflowConfigurationCatalogContentDiscoveryStepTests
{
    [Fact]
    public void DiscoverNicheContentStep_IsPresent_UnderSingleAnalyzerWorkflow()
    {
        var step = WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-discovery", "discover-niche-content");

        Assert.NotNull(step);
    }

    [Fact]
    public void DiscoverNicheContentStep_CarriesTheNicheSearchOperationKey()
    {
        var step = WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-discovery", "discover-niche-content");

        Assert.NotNull(step);
        Assert.Equal("niche-search", step!.OperationKey);
    }

    [Fact]
    public void DiscoverNicheContentStep_RequiresNetwork_SupportingTheSameNetworksAsScrapeVideo()
    {
        var step = WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-discovery", "discover-niche-content");

        Assert.NotNull(step);
        Assert.True(step!.RequiresNetwork);

        // The CONFIGURATION surface matches scrape-video's: restricting it to TikTok would be
        // artificial. What is genuinely limited today is which per-network discovery adapter
        // exists (only TikTok) — that adapter gap degrades through Unconfigured/unsupported at
        // runtime, never here at the catalog level.
        Assert.Equal(["instagram", "tiktok", "youtube"], step.SupportedNetworkKeys);
    }

    [Fact]
    public void DiscoverNicheContentStep_AllowsOnlyDataExtractionApifyProviders()
    {
        var step = WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-discovery", "discover-niche-content");

        Assert.NotNull(step);
        Assert.Contains(IntegrationProviderCategory.DataExtraction, step!.AllowedProviderCategories);
        Assert.Contains("apify", step.AllowedProviderKeys);
    }

    [Fact]
    public void DiscoverNicheContentStep_SupportsActorSelection_WithTheNicheSearchVariantKey()
    {
        var step = WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-discovery", "discover-niche-content");

        Assert.NotNull(step);
        Assert.True(step!.SupportsActorSelection);
        Assert.Equal("niche-search", step.ActorVariantKey);
    }

    [Fact]
    public void DiscoverNicheContentStep_DoesNotSupportModelSelection()
    {
        var step = WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-discovery", "discover-niche-content");

        Assert.NotNull(step);
        Assert.False(step!.SupportsModelSelection);
    }

    [Fact]
    public void ContentDiscoveryAnalysisType_IsUnderSingleAnalyzerWorkflow_NotANewTopLevelKey()
    {
        var workflows = WorkflowConfigurationCatalog.Workflows;

        // D11 (corrected): reusing the existing "single-analyzer" key, sibling to "content-analysis",
        // rather than the "generator" key or a new one — same pipeline, same configuration.
        Assert.Equal(["single-analyzer", "generator"], workflows.Select(x => x.Key));

        var singleAnalyzer = WorkflowConfigurationCatalog.FindWorkflow("single-analyzer");
        Assert.NotNull(singleAnalyzer);
        Assert.Contains(singleAnalyzer!.AnalysisTypes, x => x.Key == "content-discovery");
    }

    [Fact]
    public void ContentDiscoveryAnalysisType_HasOnlyTheDiscoveryStep_NoDuplicatedAiSteps()
    {
        var analysisType = WorkflowConfigurationCatalog.FindAnalysisType("single-analyzer", "content-discovery");

        Assert.NotNull(analysisType);

        // Same pipeline ⇒ one configuration: analyze/generate/image steps live only under
        // content-analysis and MUST NOT be duplicated here.
        Assert.Equal(["discover-niche-content"], analysisType!.Steps.Select(x => x.Key));
    }

    [Fact]
    public void AiSteps_StillPresent_UnderContentAnalysis_NotDuplicated()
    {
        Assert.NotNull(WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-analysis", "analyze-content"));
        Assert.NotNull(WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-analysis", "generate-content"));
        Assert.NotNull(WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-analysis", "generate-image"));

        Assert.Null(WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-discovery", "analyze-content"));
        Assert.Null(WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-discovery", "generate-content"));
        Assert.Null(WorkflowConfigurationCatalog.FindStep("single-analyzer", "content-discovery", "generate-image"));
    }

    [Fact]
    public void ContentDiscoveryAnalysisType_IsNoLongerUnderGeneratorWorkflow()
    {
        Assert.Null(WorkflowConfigurationCatalog.FindAnalysisType("generator", "content-discovery"));
    }

    [Fact]
    public void ExistingGeneratorSteps_StillPresent_NoRegression()
    {
        Assert.NotNull(WorkflowConfigurationCatalog.FindStep("generator", "batch-generation", "discover-reference-content"));
        Assert.NotNull(WorkflowConfigurationCatalog.FindStep("generator", "batch-generation", "generate-ideas"));
        Assert.NotNull(WorkflowConfigurationCatalog.FindStep("generator", "batch-generation", "generate-content"));
        Assert.NotNull(WorkflowConfigurationCatalog.FindStep("generator", "batch-generation", "generate-image"));
    }
}
