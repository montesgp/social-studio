using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Features.Integrations.WorkflowConfigurations;

public static class WorkflowConfigurationCatalog
{
    public static IReadOnlyList<WorkflowDefinition> Workflows { get; } =
    [
        new(
            "single-analyzer",
            "Single Analyzer",
            [
                new(
                    "content-analysis",
                    "Content analysis",
                    [
                        new(
                            "scrape-video",
                            "Resolve source content",
                            "scrape-video",
                            "Scrape video",
                            "Choose the extraction provider and actor routing for the source content lookup.",
                            RequiresNetwork: true,
                            SupportedNetworkKeys: ["instagram", "tiktok", "youtube"],
                            AllowedProviderCategories: [IntegrationProviderCategory.DataExtraction],
                            AllowedProviderKeys: [],
                            SupportsModelSelection: false,
                            SupportsActorSelection: true,
                            ActorVariantKey: "social-extraction"),
                        new(
                            "analyze-content",
                            "Analyze content with AI",
                            "ai-analysis",
                            "AI analysis",
                            "Choose the AI provider/model that analyzes the extracted content.",
                            RequiresNetwork: false,
                            SupportedNetworkKeys: [],
                            AllowedProviderCategories: [IntegrationProviderCategory.AiModel],
                            AllowedProviderKeys: [],
                            SupportsModelSelection: true,
                            SupportsActorSelection: false,
                            ActorVariantKey: null),
                        new(
                            "generate-content",
                            "Generate content with AI",
                            "ai-generation",
                            "AI generation",
                            "Choose the AI provider/model that generates the downstream content package.",
                            RequiresNetwork: false,
                            SupportedNetworkKeys: [],
                            AllowedProviderCategories: [IntegrationProviderCategory.AiModel],
                            AllowedProviderKeys: [],
                            SupportsModelSelection: true,
                            SupportsActorSelection: false,
                            ActorVariantKey: null)
                    ]),
                new(
                    "profile-analysis",
                    "Profile analysis",
                    [
                        new(
                            "scrape-profile",
                            "Resolve profile snapshot",
                            "scrape-profile",
                            "Scrape profile",
                            "Choose the extraction provider and actor routing for the profile snapshot.",
                            RequiresNetwork: true,
                            SupportedNetworkKeys: ["instagram"],
                            AllowedProviderCategories: [IntegrationProviderCategory.DataExtraction],
                            AllowedProviderKeys: ["apify"],
                            SupportsModelSelection: false,
                            SupportsActorSelection: true,
                            ActorVariantKey: "profile"),
                        new(
                            "analyze-profile",
                            "Analyze profile with AI",
                            "ai-analysis",
                            "AI analysis",
                            "Choose the AI provider/model that produces the profile analysis narrative.",
                            RequiresNetwork: false,
                            SupportedNetworkKeys: [],
                            AllowedProviderCategories: [IntegrationProviderCategory.AiModel],
                            AllowedProviderKeys: [],
                            SupportsModelSelection: true,
                            SupportsActorSelection: false,
                            ActorVariantKey: null)
                    ])
            ])
    ];

    public static WorkflowDefinition? FindWorkflow(string? workflowKey) =>
        Workflows.FirstOrDefault(x => string.Equals(x.Key, Normalize(workflowKey), StringComparison.OrdinalIgnoreCase));

    public static WorkflowAnalysisTypeDefinition? FindAnalysisType(string? workflowKey, string? analysisType) =>
        FindWorkflow(workflowKey)?.AnalysisTypes.FirstOrDefault(x =>
            string.Equals(x.Key, Normalize(analysisType), StringComparison.OrdinalIgnoreCase));

    public static WorkflowStepDefinition? FindStep(string? workflowKey, string? analysisType, string? stepKey) =>
        FindAnalysisType(workflowKey, analysisType)?.Steps.FirstOrDefault(x =>
            string.Equals(x.Key, Normalize(stepKey), StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}

public sealed record WorkflowDefinition(
    string Key,
    string Label,
    IReadOnlyList<WorkflowAnalysisTypeDefinition> AnalysisTypes);

public sealed record WorkflowAnalysisTypeDefinition(
    string Key,
    string Label,
    IReadOnlyList<WorkflowStepDefinition> Steps);

public sealed record WorkflowStepDefinition(
    string Key,
    string Label,
    string OperationKey,
    string OperationLabel,
    string Description,
    bool RequiresNetwork,
    IReadOnlyList<string> SupportedNetworkKeys,
    IReadOnlyList<IntegrationProviderCategory> AllowedProviderCategories,
    IReadOnlyList<string> AllowedProviderKeys,
    bool SupportsModelSelection,
    bool SupportsActorSelection,
    string? ActorVariantKey);
