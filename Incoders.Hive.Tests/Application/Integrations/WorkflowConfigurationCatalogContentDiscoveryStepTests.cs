using Incoders.Hive.Application.Features.Integrations.WorkflowConfigurations;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Tests.Application.Integrations;

/// <summary>
/// WU-6 RED tests (task 6.10) for the <c>content-discovery</c> analysis type and its
/// <c>discover-niche-content</c> step, added under the EXISTING <c>generator</c> workflow key
/// (design D11 — not a new top-level workflow key, so the settings selector, which is built from
/// checklist groups, renders the stage without a new group). Refs: INT-DISC-02.
/// </summary>
public sealed class WorkflowConfigurationCatalogContentDiscoveryStepTests
{
    [Fact]
    public void DiscoverNicheContentStep_IsPresent_UnderExistingGeneratorWorkflow()
    {
        var step = WorkflowConfigurationCatalog.FindStep("generator", "content-discovery", "discover-niche-content");

        Assert.NotNull(step);
    }

    [Fact]
    public void DiscoverNicheContentStep_CarriesTheNicheSearchOperationKey()
    {
        var step = WorkflowConfigurationCatalog.FindStep("generator", "content-discovery", "discover-niche-content");

        Assert.NotNull(step);
        Assert.Equal("niche-search", step!.OperationKey);
    }

    [Fact]
    public void DiscoverNicheContentStep_RequiresNetwork_SupportingOnlyTikTok()
    {
        var step = WorkflowConfigurationCatalog.FindStep("generator", "content-discovery", "discover-niche-content");

        Assert.NotNull(step);
        Assert.True(step!.RequiresNetwork);
        Assert.Equal(["tiktok"], step.SupportedNetworkKeys);
    }

    [Fact]
    public void DiscoverNicheContentStep_AllowsOnlyDataExtractionApifyProviders()
    {
        var step = WorkflowConfigurationCatalog.FindStep("generator", "content-discovery", "discover-niche-content");

        Assert.NotNull(step);
        Assert.Contains(IntegrationProviderCategory.DataExtraction, step!.AllowedProviderCategories);
        Assert.Contains("apify", step.AllowedProviderKeys);
    }

    [Fact]
    public void DiscoverNicheContentStep_SupportsActorSelection_WithTheNicheSearchVariantKey()
    {
        var step = WorkflowConfigurationCatalog.FindStep("generator", "content-discovery", "discover-niche-content");

        Assert.NotNull(step);
        Assert.True(step!.SupportsActorSelection);
        Assert.Equal("niche-search", step.ActorVariantKey);
    }

    [Fact]
    public void DiscoverNicheContentStep_DoesNotSupportModelSelection()
    {
        var step = WorkflowConfigurationCatalog.FindStep("generator", "content-discovery", "discover-niche-content");

        Assert.NotNull(step);
        Assert.False(step!.SupportsModelSelection);
    }

    [Fact]
    public void ContentDiscoveryAnalysisType_IsUnderGeneratorWorkflow_NotANewTopLevelKey()
    {
        var workflows = WorkflowConfigurationCatalog.Workflows;

        // D11: reusing the existing "generator" key rather than introducing a new one.
        Assert.Equal(["single-analyzer", "generator"], workflows.Select(x => x.Key));

        var generator = WorkflowConfigurationCatalog.FindWorkflow("generator");
        Assert.NotNull(generator);
        Assert.Contains(generator!.AnalysisTypes, x => x.Key == "content-discovery");
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
