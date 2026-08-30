using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Features.Social.SingleAnalyzer.Analyze;

namespace Incoders.Hive.ApiService.BackgroundServices;

/// <summary>
/// Consumes queued single-analysis jobs and runs the heavy extraction + AI work in its OWN scope,
/// decoupled from any HTTP request (so a client disconnect never cancels the work). Concurrency is
/// capped so many simultaneous users queue up without overloading providers/DB.
/// </summary>
internal sealed class SingleAnalysisBackgroundWorker : BackgroundService
{
    private const int MaxConcurrency = 3;

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

        try
        {
            await Parallel.ForEachAsync(_queue.DequeueAllAsync(stoppingToken), options, ProcessAsync);
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
