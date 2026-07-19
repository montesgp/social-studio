using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.ApiService.BackgroundServices;

/// <summary>
/// Consumes queued content-generation jobs and delegates each one to
/// <see cref="ContentGenerationJobProcessor"/> inside a fresh DI scope. The processor owns the
/// end-to-end pipeline (load analysis → orchestrate → compose + render → build + store).
///
/// Mirrors the pattern of <see cref="SingleAnalysisBackgroundWorker"/>.
///
/// <para>Resilience against "stuck generating forever" (the pill never changes):</para>
/// <list type="bullet">
///   <item><description>Startup: any job left in "Running" (orphaned from a crash) is failed.</description></item>
///   <item><description>Per-job: a wall-clock timeout cancels a job that runs past the configured
///   <see cref="ContentGenerationWorkerOptions.JobTimeout"/>; the processor records the terminal
///   status so the UI stops polling.</description></item>
///   <item><description>Periodic watchdog: a recurring sweep fails any job stuck in "Running" past
///   <see cref="ContentGenerationJobWatchdog.StuckJobThreshold"/> (covers a worker that died
///   without unwinding its catch).</description></item>
/// </list>
/// The "which jobs to fail" policy lives in <see cref="ContentGenerationJobWatchdog"/> (Application);
/// this worker only owns the "when" (startup + timer) and the per-job timeout.
/// </summary>
internal sealed class ContentGenerationBackgroundWorker : BackgroundService
{
    private const int MaxConcurrency = 3;

    /// <summary>How often the watchdog sweeps for stuck "Running" jobs.</summary>
    internal static readonly TimeSpan WatchdogInterval = TimeSpan.FromMinutes(2);

    private readonly IContentGenerationJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ContentGenerationBackgroundWorker> _logger;
    private readonly TimeSpan _jobTimeout;

    public ContentGenerationBackgroundWorker(
        IContentGenerationJobQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<ContentGenerationWorkerOptions> workerOptions,
        ILogger<ContentGenerationBackgroundWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _jobTimeout = workerOptions.Value.JobTimeout;

        // Fail fast on startup rather than letting a misconfigured JobTimeout silently let the
        // periodic watchdog sweep reap jobs that are still legitimately running.
        ContentGenerationJobWatchdog.EnsureJobTimeoutBelowStuckThreshold(_jobTimeout);
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
            await ContentGenerationJobWatchdog.MarkOrphanedRunningJobsAsync(jobRepo, stoppingToken);
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

        // Run the job consumer and the periodic watchdog concurrently. Both end on host shutdown.
        var consume = Parallel.ForEachAsync(_queue.DequeueAllAsync(stoppingToken), options, ProcessAsync);
        var watchdog = RunWatchdogAsync(stoppingToken);

        try
        {
            await Task.WhenAll(consume, watchdog);
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

        // Per-job wall-clock timeout, linked to the host token. When it fires the processor's
        // cancellation handler records the terminal status (using a fresh token), so the job
        // never stays "Running".
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        jobCts.CancelAfter(_jobTimeout);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<ContentGenerationJobProcessor>();

            // Gap 1 (incident job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1, Change 5): pass the outer host
            // token alongside jobCts.Token so the processor's terminal-cancellation write can tell a
            // per-job budget timeout apart from host shutdown — the same distinction the catch block
            // below already uses to decide whether to log "TIMED OUT", now carried into the DB record.
            var cancellationContext = new ContentGenerationJobCancellationContext(cancellationToken, _jobTimeout);
            await processor.ProcessAsync(jobId, jobCts.Token, cancellationContext);

            sw.Stop();
            _logger.LogInformation(
                "ContentGeneration worker: FINISHED job {JobId} in {ElapsedMs} ms.",
                jobId, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Per-job timeout (NOT host shutdown). The processor already marked the job Failed.
            // Swallow so the parallel consumer keeps draining the queue.
            sw.Stop();
            _logger.LogError(
                "ContentGeneration worker: job {JobId} TIMED OUT after {ElapsedMs} ms (limit {LimitMs} ms).",
                jobId, sw.ElapsedMilliseconds, (long)_jobTimeout.TotalMilliseconds);
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
    /// Periodic sweep that force-fails jobs stuck in "Running" past the watchdog threshold.
    /// This is the backstop for a worker that died without running its own cancellation handler.
    /// </summary>
    private async Task RunWatchdogAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(WatchdogInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var jobRepo = scope.ServiceProvider.GetRequiredService<IContentGenerationJobRepository>();
                    var failed = await ContentGenerationJobWatchdog.FailStuckRunningJobsAsync(
                        jobRepo, DateTime.UtcNow, stoppingToken);
                    if (failed > 0)
                    {
                        _logger.LogWarning(
                            "ContentGeneration watchdog: force-failed {Count} job(s) stuck in Running past {Minutes} min.",
                            failed, (int)ContentGenerationJobWatchdog.StuckJobThreshold.TotalMinutes);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ContentGeneration watchdog: sweep failed (continuing).");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down — expected.
        }
    }
}
