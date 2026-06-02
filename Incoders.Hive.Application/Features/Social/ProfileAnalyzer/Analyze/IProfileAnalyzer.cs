namespace Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;

/// <summary>
/// Port for the profile analysis orchestrator. The orchestrator executes the full
/// scrape → stats → AI → persist pipeline for a queued profile analysis job.
/// Implemented by <see cref="ProfileAnalyzerOrchestrator"/> in the Application layer.
/// </summary>
public interface IProfileAnalyzer
{
    /// <summary>
    /// Background: loads the analysis record and runs the full pipeline:
    /// SetStatus(ScrapingProfile) → IProfileScrapeResolver → owner-filter + IProfileMetricCalculator
    /// → SetStatus(ProfileScraped|ProfileScrapedPartial) → persist raw posts + stats
    /// → SetStatus(Analyzing) → IAiCompletionGateway.CompleteJsonAsync&lt;ProfileAnalysisResult&gt;
    /// → SetStatus(Analyzed|Failed).
    /// Emits structured log entries at each stage boundary (PA-23).
    /// </summary>
    Task RunQueuedAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
