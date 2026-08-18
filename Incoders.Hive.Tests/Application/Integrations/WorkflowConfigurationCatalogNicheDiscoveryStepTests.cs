using Incoders.Hive.Application.Features.Integrations.WorkflowConfigurations;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.NicheDiscovery;
using Incoders.Hive.Infrastructure.Social.NicheDiscovery;

namespace Incoders.Hive.Tests.Application.Integrations;

/// <summary>
/// WU-4 (INT-ND-04): a new top-level workflow key <c>niche-discovery</c> with analysis type
/// <c>market-opportunity</c> and one step per <see cref="Incoders.Hive.Domain.Social.NicheDiscovery.DiscoveryStage"/>
/// (design §5.2). This is a NEW top-level key, not a step under <c>single-analyzer</c>/<c>generator</c>
/// (N12) — the operator has no client yet when starting a discovery run.
/// </summary>
public sealed class WorkflowConfigurationCatalogNicheDiscoveryStepTests
{
    private static readonly string[] ExpectedStepKeys =
    [
        "channel-search",
        "video-search",
        "channel-detail",
        "video-detail",
        "trending-sweep",
    ];

    [Fact]
    public void NicheDiscoveryWorkflow_IsATopLevelKey()
    {
        var workflow = WorkflowConfigurationCatalog.FindWorkflow("niche-discovery");

        Assert.NotNull(workflow);
    }

    [Fact]
    public void MarketOpportunityAnalysisType_IsUnderNicheDiscoveryWorkflow_WithOneStepPerDiscoveryStage()
    {
        var analysisType = WorkflowConfigurationCatalog.FindAnalysisType("niche-discovery", "market-opportunity");

        Assert.NotNull(analysisType);
        Assert.Equal(ExpectedStepKeys, analysisType!.Steps.Select(x => x.Key));
    }

    [Theory]
    [InlineData("channel-search")]
    [InlineData("video-search")]
    [InlineData("channel-detail")]
    [InlineData("video-detail")]
    [InlineData("trending-sweep")]
    public void EachStep_RequiresNetwork_SupportsOnlyYouTube_AndAllowsOfficialOrDataExtractionProviders(string stepKey)
    {
        var step = WorkflowConfigurationCatalog.FindStep("niche-discovery", "market-opportunity", stepKey);

        Assert.NotNull(step);
        Assert.True(step!.RequiresNetwork);
        Assert.Equal(["youtube"], step.SupportedNetworkKeys);
        Assert.Equal(
            [IntegrationProviderCategory.DataExtraction],
            step.AllowedProviderCategories);
    }

    /// <summary>
    /// Every stage the origin can resolve MUST be declarable in the catalog, or the operator has no way
    /// to enable it and the run dies with origin_unconfigured on a step they were never offered. This
    /// test walks the DiscoveryStage enum so a stage added later cannot silently skip the catalog.
    /// </summary>
    [Fact]
    public void EveryDiscoveryStage_HasADeclarableCatalogStep()
    {
        foreach (var stage in Enum.GetValues<DiscoveryStage>())
        {
            var stepKey = NicheDiscoveryOriginConfiguration.StepKey(stage);
            var step = WorkflowConfigurationCatalog.FindStep("niche-discovery", "market-opportunity", stepKey);

            Assert.True(
                step is not null,
                $"DiscoveryStage.{stage} resolves step key '{stepKey}', which the workflow catalog does not "
                    + "declare — the operator cannot enable it, so any run reaching that stage fails as "
                    + "origin_unconfigured.");
        }
    }

    [Fact]
    public void NicheDiscoveryWorkflow_DoesNotDuplicateExistingWorkflowKeys()
    {
        var workflows = WorkflowConfigurationCatalog.Workflows;

        Assert.Equal(["single-analyzer", "generator", "niche-discovery"], workflows.Select(x => x.Key));
    }
}
