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
                            ActorVariantKey: null),
                        new(
                            "generate-image",
                            "Generate images with AI",
                            "ai-image",
                            "AI image generation",
                            "OPTIONAL. Choose the AI image provider/model that generates raster backgrounds for " +
                            "ClientAesthetic carousel creative slides. Leave unconfigured to use CSS designer " +
                            "backgrounds for all slides.",
                            RequiresNetwork: false,
                            SupportedNetworkKeys: [],
                            AllowedProviderCategories: [IntegrationProviderCategory.MediaGeneration],
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
                            ActorVariantKey: null),
                        new(
                            "extract-aesthetic",
                            "Learn the client's visual aesthetic with AI",
                            "ai-aesthetic",
                            "AI aesthetic extraction",
                            "OPTIONAL. Choose a MULTIMODAL AI provider/model that learns the client's visual " +
                            "style (palette, mood, composition, motifs, voice) from their recent posts so " +
                            "ClientAesthetic generation can emulate it. Leave unconfigured to skip aesthetic " +
                            "extraction (generation falls back to text-descriptor guidance).",
                            RequiresNetwork: false,
                            SupportedNetworkKeys: [],
                            AllowedProviderCategories: [IntegrationProviderCategory.AiModel],
                            AllowedProviderKeys: [],
                            SupportsModelSelection: true,
                            SupportsActorSelection: false,
                            ActorVariantKey: null)
                    ]),

                // Content discovery — mines candidate high-performing content for the operator to
                // review and select one at a time (content-discovery-intelligence). This is a
                // SIBLING of content-analysis, not a copy of it: once the operator-selected candidate
                // becomes a SingleContentAnalysis, the downstream pipeline (CreateContentGenerationBatchCommand,
                // GenerateContentPackageCommand) anchors to that AnalysisId, not to a URL — it is
                // IDENTICAL to the by-URL content-analysis flow. Same pipeline means the SAME
                // configuration: this analysis type therefore carries ONLY the discovery step
                // (discover-niche-content); the AI analysis/generation/image steps are intentionally
                // NOT duplicated here — they are reused from content-analysis (design D11,
                // INT-DISC-02/03). Placing it under "single-analyzer" also matches the operator's
                // mental model: it is a way of finding a candidate to feed the Single Analyzer, not a
                // second Content Generator tool.
                //
                // NOTE — a workflow key still needs a matching checklistGroups entry (currently
                // "single-analyzer" + "generator") to render as configurable in the settings UI; a
                // workflow key with no matching group renders "not configurable yet".
                new(
                    "content-discovery",
                    "Content discovery",
                    [
                        new(
                            "discover-niche-content",
                            "Discover niche content candidates",
                            "niche-search",
                            "Niche search",
                            "Choose the extraction provider and actor used to search for candidate " +
                            "high-performing content in the client's niche. No actor is forced by the " +
                            "platform — configure and enable an actor here to turn the discovery stage " +
                            "on. The CONFIGURATION surface matches scrape-video's: restricting it to " +
                            "TikTok would be artificial. What is genuinely limited is which per-network " +
                            "discovery adapter exists today (only TikTok) — a network configured " +
                            "without a working adapter degrades through the Unconfigured/unsupported " +
                            "path at runtime, never a crash.",
                            RequiresNetwork: true,
                            SupportedNetworkKeys: ["instagram", "tiktok", "youtube"],
                            AllowedProviderCategories: [IntegrationProviderCategory.DataExtraction],
                            AllowedProviderKeys: ["apify"],
                            SupportsModelSelection: false,
                            SupportsActorSelection: true,
                            ActorVariantKey: "niche-search")
                    ])
            ]),

        // Content Generator — the batch ("Magic Generator") flow.
        //
        // The key MUST be "generator": the settings UI builds its workflow selector from the overview
        // checklist groups, and that group is keyed "generator". A mismatched key here leaves the tool
        // rendering "not configurable yet" even though a catalog exists.
        //
        // The steps mirror how a batch is actually produced: ideas are gathered first (each idea
        // source is routable on its own, so an organization can pay for scraping but not for an idea
        // agent, or the reverse), then each piece is written and optionally illustrated.
        new(
            "generator",
            "Content Generator",
            [
                new(
                    "batch-generation",
                    "Batch generation",
                    [
                        new(
                            "discover-reference-content",
                            "Mine reference accounts for winning content",
                            "profile",
                            "Scrape profile",
                            "OPTIONAL. Choose the extraction provider and actor used to pull recent posts from " +
                            "the client's reference accounts, so batch ideas can be drawn from content that " +
                            "actually performed. Leave unconfigured to skip this idea source.",
                            RequiresNetwork: true,
                            SupportedNetworkKeys: ["instagram"],
                            AllowedProviderCategories: [IntegrationProviderCategory.DataExtraction],
                            AllowedProviderKeys: ["apify"],
                            SupportsModelSelection: false,
                            SupportsActorSelection: true,
                            ActorVariantKey: "profile"),
                        new(
                            "generate-ideas",
                            "Propose content ideas with AI",
                            "ai-generation",
                            "AI generation",
                            "Choose the AI provider/model that proposes the topics a batch is built from, " +
                            "grounded in the client's strategy and in everything else known about them.",
                            RequiresNetwork: false,
                            SupportedNetworkKeys: [],
                            AllowedProviderCategories: [IntegrationProviderCategory.AiModel],
                            AllowedProviderKeys: [],
                            SupportsModelSelection: true,
                            SupportsActorSelection: false,
                            ActorVariantKey: null),
                        new(
                            "generate-content",
                            "Generate each piece with AI",
                            "ai-generation",
                            "AI generation",
                            "Choose the AI provider/model that writes each piece of the batch from its idea.",
                            RequiresNetwork: false,
                            SupportedNetworkKeys: [],
                            AllowedProviderCategories: [IntegrationProviderCategory.AiModel],
                            AllowedProviderKeys: [],
                            SupportsModelSelection: true,
                            SupportsActorSelection: false,
                            ActorVariantKey: null),
                        new(
                            "generate-image",
                            "Generate images with AI",
                            "ai-image",
                            "AI image generation",
                            "OPTIONAL. Choose the AI image provider/model that renders raster backgrounds for " +
                            "the batch's visual pieces. Leave unconfigured to use CSS designer backgrounds.",
                            RequiresNetwork: false,
                            SupportedNetworkKeys: [],
                            AllowedProviderCategories: [IntegrationProviderCategory.MediaGeneration],
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
