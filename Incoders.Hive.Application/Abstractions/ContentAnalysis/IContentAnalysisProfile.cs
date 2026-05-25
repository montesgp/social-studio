using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Application.Abstractions.ContentAnalysis;

/// <summary>
/// Module-neutral profile that describes the content-analysis flow for one specific module
/// (e.g. Social Single Analyzer, future E-Commerce analyzer).
/// Contains only metadata constants and pure behavior — no provider, HTTP, or EF types.
/// </summary>
public interface IContentAnalysisProfile
{
    /// <summary>The flow key that identifies this module's analysis flow (e.g. "single-analyzer").</summary>
    string FlowKey { get; }

    /// <summary>The capability key used to route content extraction (e.g. "social-extraction").</summary>
    string ExtractionCapabilityKey { get; }

    /// <summary>
    /// The integration provider key used for content extraction (e.g. "apify").
    /// Extraction resolves this connected provider directly — it does NOT use a routing policy.
    /// </summary>
    string ExtractionProviderKey { get; }

    /// <summary>The capability key used to route AI completion (e.g. "ai-analysis").</summary>
    string AiCapabilityKey { get; }

    /// <summary>The AI prompt version key (e.g. "single-analyzer.analysis.v1").</summary>
    string PromptVersionKey { get; }

    /// <summary>
    /// Detects the platform key ("tiktok", "instagram", "unknown") from a raw source URL.
    /// Delegates to the appropriate Domain value object (e.g. SingleAnalyzerSourceUrl.Create).
    /// </summary>
    string DetectPlatform(string sourceUrl);
}

/// <summary>
/// Resolves the correct <see cref="IContentAnalysisProfile"/> for a given flow key.
/// </summary>
public interface IContentAnalysisProfileProvider
{
    /// <summary>
    /// Returns the profile registered for <paramref name="flowKey"/>,
    /// or a failure result if no profile is registered.
    /// </summary>
    Result<IContentAnalysisProfile> ForFlow(string flowKey);
}
