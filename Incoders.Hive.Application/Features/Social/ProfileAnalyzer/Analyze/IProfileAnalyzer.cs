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

    /// <summary>
    /// Re-runs ONLY the AI stage (stages 5-7) of a previously-failed analysis using the
    /// already-persisted scrape data. Does NOT call IProfileScrapeResolver — no Apify cost.
    /// Requires that the record is in <see cref="Domain.Social.ProfileAnalysis.ProfileAnalysisStatus.Analyzing"/>
    /// state (the command handler calls RestartAnalysis before enqueuing the job) and that
    /// the persisted scrape data is complete (ProfileSnapshotJson, RawPostsDatasetJson, ServerDerivedStatsJson).
    /// </summary>
    Task RunAnalysisOnlyAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
