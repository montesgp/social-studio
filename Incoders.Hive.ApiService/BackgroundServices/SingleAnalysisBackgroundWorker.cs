using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.SingleAnalyzer.Analyze;

namespace Incoders.Hive.ApiService.BackgroundServices;

/// <summary>
/// Consumes queued single-analysis jobs and runs the heavy extraction + AI work in its OWN scope,
/// decoupled from any HTTP request (so a client disconnect never cancels the work). Concurrency is
/// capped so many simultaneous users queue up without overloading providers/DB.
///
/// <para>
/// Also runs <see cref="SingleAnalysisJobWatchdog"/> on a periodic timer: the job queue
/// (<c>ChannelSingleAnalysisJobQueue</c>) is in-memory, so an enqueued job is lost if the process
/// restarts between StartAsync's enqueue and this worker dequeuing it — the analysis record would
/// otherwise sit in a non-terminal status forever, blocking the org-scoped duplicate gate with no
/// way to retry (reproduced in logs/hive-api.log 2026-09-09).
/// </para>
/// </summary>
internal sealed class SingleAnalysisBackgroundWorker : BackgroundService
{
    private const int MaxConcurrency = 3;

    /// <summary>How often the watchdog sweeps for stalled non-terminal analyses.</summary>
    internal static readonly TimeSpan WatchdogInterval = TimeSpan.FromMinutes(2);

    private readonly ISingleAnalysisJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SingleAnalysisBackgroundWorker> _logger;

    public SingleAnalysisBackgroundWorker(
        ISingleAnalysisJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<SingleAnalysisBackgroundWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxConcurrency,
            CancellationToken = stoppingToken,
        };

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

    /// <summary>
    /// Periodic sweep that fails analyses stalled in a non-terminal status past
    /// <see cref="SingleAnalysisJobWatchdog.StuckThreshold"/> — the backstop for a lost/orphaned
    /// queue entry (see class remarks).
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
                    var analyses = scope.ServiceProvider.GetRequiredService<ISingleContentAnalysisRepository>();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var failed = await SingleAnalysisJobWatchdog.FailStalledAnalysesAsync(
                        analyses, unitOfWork, DateTime.UtcNow, stoppingToken);
                    if (failed > 0)
                    {
                        _logger.LogWarning(
                            "Single Analyzer watchdog: force-failed {Count} analysis(es) stalled past {Minutes} min.",
                            failed, (int)SingleAnalysisJobWatchdog.StuckThreshold.TotalMinutes);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Single Analyzer watchdog: sweep failed (continuing).");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down — expected.
        }
    }

    private async ValueTask ProcessAsync(SingleAnalysisJob job, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation(
            "Single Analyzer worker: PROCESSING job {AnalysisId} (org {OrganizationId}) kind={Kind}.",
            job.AnalysisId,
            job.OrganizationId,
            job.Kind);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var analyzer = scope.ServiceProvider.GetRequiredService<ISingleContentAnalyzer>();
            switch (job.Kind)
            {
                case SingleAnalysisJobKind.RetryAi:
                    await analyzer.RetryAiOnlyAsync(job.AnalysisId, job.OrganizationId, cancellationToken);
                    break;
                case SingleAnalysisJobKind.Full:
                default:
                    await analyzer.RunQueuedAsync(job.AnalysisId, job.OrganizationId, cancellationToken);
                    break;
            }
            stopwatch.Stop();
            _logger.LogInformation(
                "Single Analyzer worker: FINISHED job {AnalysisId} (org {OrganizationId}) kind={Kind} in {ElapsedMs} ms.",
                job.AnalysisId,
                job.OrganizationId,
                job.Kind,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            // RunQueuedAsync persists Failed on its own errors; this guards the worker loop itself.
            _logger.LogError(
                ex,
                "Single Analyzer worker: UNHANDLED EXCEPTION on job {AnalysisId} (org {OrganizationId}) after {ElapsedMs} ms. ExceptionType={ExceptionType} Message={Message} InnerType={InnerType} InnerMessage={InnerMessage}",
                job.AnalysisId,
                job.OrganizationId,
                stopwatch.ElapsedMilliseconds,
                ex.GetType().FullName,
                ex.Message,
                ex.InnerException?.GetType().FullName,
                ex.InnerException?.Message);
        }
    }
}
