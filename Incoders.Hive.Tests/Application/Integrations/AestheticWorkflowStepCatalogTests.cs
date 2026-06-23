using Incoders.Hive.Application.Features.Integrations.WorkflowConfigurations;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Tests.Application.Integrations;

/// <summary>
/// Phase 2: the optional aesthetic-extraction step is present in the workflow catalog under
/// single-analyzer/profile-analysis as a multimodal AiModel step (CAG2-PA-03, design §0 OQ).
/// </summary>
public sealed class AestheticWorkflowStepCatalogTests
{
    [Fact]
    public void ExtractAestheticStep_IsPresent_AsOptionalMultimodalAiStep()
    {
        var step = WorkflowConfigurationCatalog.FindStep("single-analyzer", "profile-analysis", "extract-aesthetic");

        Assert.NotNull(step);
        Assert.Equal("ai-aesthetic", step!.OperationKey);
        Assert.True(step.SupportsModelSelection);
        Assert.False(step.SupportsActorSelection);
        Assert.Contains(IntegrationProviderCategory.AiModel, step.AllowedProviderCategories);
    }

    [Fact]
    public void ExistingProfileAnalysisSteps_StillPresent_NoRegression()
    {
        Assert.NotNull(WorkflowConfigurationCatalog.FindStep("single-analyzer", "profile-analysis", "scrape-profile"));
        Assert.NotNull(WorkflowConfigurationCatalog.FindStep("single-analyzer", "profile-analysis", "analyze-profile"));
    }
}
