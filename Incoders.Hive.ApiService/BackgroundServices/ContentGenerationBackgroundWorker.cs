using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;

namespace Incoders.Hive.ApiService.BackgroundServices;

/// <summary>
/// Consumes queued content-generation jobs and delegates each one to
/// <see cref="ContentGenerationJobProcessor"/> inside a fresh DI scope. The processor owns the
/// end-to-end pipeline (load analysis → orchestrate → compose + render → build + store).
///
/// Mirrors the pattern of <see cref="SingleAnalysisBackgroundWorker"/>.
/// On startup, any job still in "Running" status (orphaned from a previous crash) is
/// transitioned to "Failed" with error_code="restart-detected".
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
        // Startup orphan cleanup is best-effort. If it fails (e.g. tables not yet migrated
        // when running in fast-startup mode), log and continue — never crash the host, since
        // BackgroundServiceExceptionBehavior=StopHost would take down every endpoint.
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var jobRepo = scope.ServiceProvider.GetRequiredService<IContentGenerationJobRepository>();
            await MarkOrphanedJobsAsync(jobRepo, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ContentGeneration worker: startup orphan cleanup failed (continuing). " +
                "If this is a missing-table error, apply migrations (run without HIVE_FAST_STARTUP once).");
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
        _logger.LogInformation("ContentGeneration worker: START job {JobId}.", jobId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<ContentGenerationJobProcessor>();
            await processor.ProcessAsync(jobId, cancellationToken);

            sw.Stop();
            _logger.LogInformation(
                "ContentGeneration worker: FINISHED job {JobId} in {ElapsedMs} ms.",
                jobId, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogError(
                ex,
                "ContentGeneration worker: UNHANDLED EXCEPTION on job {JobId} after {ElapsedMs} ms.",
                jobId, sw.ElapsedMilliseconds);
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
}
