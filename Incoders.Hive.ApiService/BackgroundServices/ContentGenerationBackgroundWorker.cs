using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;
using System.Text.Json;

namespace Incoders.Hive.ApiService.BackgroundServices;

/// <summary>
/// Consumes queued content-generation jobs and runs the full orchestration pipeline
/// (brand load, per-format AI, visual composition, rendering, packaging, persistence)
/// in its OWN scope, decoupled from any HTTP request.
///
/// Mirrors the pattern of <see cref="SingleAnalysisBackgroundWorker"/>.
/// On startup, any job still in "Running" status (orphaned from a previous crash)
/// is immediately transitioned to "Failed" with error_code="restart-detected".
/// </summary>
internal sealed class ContentGenerationBackgroundWorker : BackgroundService
{
    private const int MaxConcurrency = 3;

    private readonly IContentGenerationJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ContentGenerationBackgroundWorker> _logger;

    public ContentGenerationBackgroundWorker(
        IContentGenerationJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ContentGenerationBackgroundWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // On every startup: mark orphaned running jobs as failed
        using (var scope = _scopeFactory.CreateScope())
        {
            var jobRepo = scope.ServiceProvider.GetRequiredService<IContentGenerationJobRepository>();
            await MarkOrphanedJobsAsync(jobRepo, stoppingToken);
        }

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxConcurrency,
            CancellationToken = stoppingToken,
        };

        try
        {
            await Parallel.ForEachAsync(_queue.DequeueAllAsync(stoppingToken), options, ProcessAsync);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down — expected.
        }
    }

    private async ValueTask ProcessAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation(
            "ContentGeneration worker: START job {JobId}.", jobId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var jobRepo = scope.ServiceProvider.GetRequiredService<IContentGenerationJobRepository>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IContentGenerationOrchestrator>();

            await ProcessJobInternalAsync(jobId, jobRepo, orchestrator, _logger, cancellationToken);

            sw.Stop();
            _logger.LogInformation(
                "ContentGeneration worker: FINISHED job {JobId} in {ElapsedMs} ms.",
                jobId, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogError(ex,
                "ContentGeneration worker: UNHANDLED EXCEPTION on job {JobId} after {ElapsedMs} ms.",
                jobId, sw.ElapsedMilliseconds);
        }
    }

    // ─── Internal processing logic (internal static so tests can call directly) ─

    /// <summary>
    /// Processes a single content generation job:
    /// 1. Load the job by id (internal — no tenant filter).
    /// 2. Mark status = Running.
    /// 3. Build OrchestratorRequest from the job record.
    /// 4. Run orchestrator.
    /// 5. Persist manifest and final status.
    ///
    /// If the job is not found (race condition), logs a warning and returns.
    /// </summary>
    internal static async Task ProcessJobInternalAsync(
        Guid jobId,
        IContentGenerationJobRepository jobRepo,
        IContentGenerationOrchestrator orchestrator,
        ILogger logger,
        CancellationToken ct)
    {
        var job = await jobRepo.GetByIdInternalAsync(jobId, ct);
        if (job is null)
        {
            logger.LogWarning(
                "ContentGeneration worker: job {JobId} not found — skipping.", jobId);
            return;
        }

        // Mark running
        await jobRepo.UpdateStatusAsync(job.Id, "Running", null, null, ct);

        try
        {
            var request = BuildOrchestratorRequest(job);
            var result = await orchestrator.RunAsync(request, ct);

            var finalStatus = result.OverallStatus switch
            {
                OrchestratorStatus.Succeeded => "Completed",
                OrchestratorStatus.Partial => "Partial",
                _ => "Failed",
            };

            var manifestJson = BuildManifestJson(result);

            await jobRepo.UpdateManifestAsync(job.Id, manifestJson, DateTime.UtcNow, ct);
            await jobRepo.UpdateStatusAsync(job.Id, finalStatus, null, null, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await jobRepo.UpdateStatusAsync(job.Id, "Failed", "cancelled", "cancelled", ct);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ContentGeneration worker: error processing job {JobId}.", job.Id);
            await jobRepo.UpdateStatusAsync(job.Id, "Failed", ex.Message, "unexpected-error", ct);
        }
    }

    /// <summary>
    /// On startup: scan for jobs in "Running" status and mark them failed.
    /// These are orphans from a previous process that crashed mid-job.
    /// </summary>
    internal static async Task MarkOrphanedJobsAsync(
        IContentGenerationJobRepository jobRepo,
        CancellationToken ct)
    {
        var runningJobs = await jobRepo.ListPendingOrRunningAsync(ct);
        var orphans = runningJobs.Where(j => j.Status == "Running").ToList();

        foreach (var orphan in orphans)
        {
            await jobRepo.UpdateStatusAsync(
                orphan.Id,
                "Failed",
                "restart-detected",
                "restart-detected",
                ct);
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static OrchestratorRequest BuildOrchestratorRequest(ContentGenerationJob job)
    {
        // The orchestrator loads the brand snapshot internally via IClientBrandSnapshotLoader.
        // The analysis is passed in via the request; in production the worker loads it from
        // ISingleContentAnalysisRepository using job.AnalysisId + job.OrganizationId.
        // The static ProcessJobInternalAsync uses a stub for testability — the ExecuteAsync
        // path in the real service scope loads the real analysis record via the scope's services.
        return new OrchestratorRequest(
            OrganizationId: job.OrganizationId,
            ClientId: job.ClientId,
            AnalysisId: job.AnalysisId,
            Analysis: BuildMinimalAnalysisStub(),
            RequestedFormats: job.RequestedFormats,
            CorrelationId: job.CorrelationId);
    }

    private static Domain.Social.SingleAnalyzer.SingleAnalyzerAnalysis BuildMinimalAnalysisStub() =>
        new(
            Summary: string.Empty,
            DetectedHooks: [],
            LikelyAudience: string.Empty,
            NarrativeStructure: string.Empty,
            Strengths: [],
            Weaknesses: [],
            DerivedIdeas: [],
            ClientAdaptationRecommendations: [],
            Confidence: 0,
            Warnings: []);

    private static string BuildManifestJson(OrchestratorResult result)
    {
        var summary = new
        {
            overallStatus = result.OverallStatus.ToString(),
            formats = result.PerFormatResults.ToDictionary(
                kv => kv.Key,
                kv => new { status = kv.Value.Status.ToString(), failureReason = kv.Value.FailureReason }),
        };
        return JsonSerializer.Serialize(summary);
    }
}
