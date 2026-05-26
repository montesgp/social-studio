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
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var analyzer = scope.ServiceProvider.GetRequiredService<ISingleContentAnalyzer>();
            await analyzer.RunQueuedAsync(job.AnalysisId, job.OrganizationId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // RunQueuedAsync persists Failed on its own errors; this guards the worker loop itself.
            _logger.LogError(
                ex,
                "Single Analyzer background job failed for analysis {AnalysisId} (org {OrganizationId}).",
                job.AnalysisId,
                job.OrganizationId);
        }
    }
}
