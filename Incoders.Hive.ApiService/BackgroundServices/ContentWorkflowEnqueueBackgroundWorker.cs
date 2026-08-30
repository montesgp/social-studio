using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.Workflow.Sequencing;

namespace Incoders.Hive.ApiService.BackgroundServices;

/// <summary>
/// Consumes queued content-workflow enqueue items and runs each candidate's independent
/// analysis-then-batch sequence in its OWN scope, mirroring
/// <see cref="SingleAnalysisBackgroundWorker"/> (design decision #3/#4: queue + hosted worker, fan-out
/// per candidate so sibling failure isolation falls out of the loop rather than needing explicit
/// coordination).
/// </summary>
internal sealed class ContentWorkflowEnqueueBackgroundWorker : BackgroundService
{
    private const int MaxConcurrency = 3;

    private readonly IContentWorkflowEnqueueQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ContentWorkflowEnqueueBackgroundWorker> _logger;

    public ContentWorkflowEnqueueBackgroundWorker(
        IContentWorkflowEnqueueQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ContentWorkflowEnqueueBackgroundWorker> logger)
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

    private async ValueTask ProcessAsync(ContentWorkflowEnqueueItem item, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation(
            "Content workflow enqueue worker: PROCESSING candidate {CandidateId} (session {SessionId}, org {OrganizationId}).",
            item.CandidateId, item.SessionId, item.OrganizationId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sequencer = scope.ServiceProvider.GetRequiredService<ContentWorkflowEnqueueSequencer>();
            await sequencer.RunAsync(item, cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation(
                "Content workflow enqueue worker: FINISHED candidate {CandidateId} (session {SessionId}) in {ElapsedMs} ms.",
                item.CandidateId, item.SessionId, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            // The sequencer already persists a terminal candidate status on its own business failures;
            // this guards the worker loop itself against an unexpected exception taking a sibling item
            // down with it.
            _logger.LogError(
                ex,
                "Content workflow enqueue worker: UNHANDLED EXCEPTION on candidate {CandidateId} (session {SessionId}) after {ElapsedMs} ms.",
                item.CandidateId, item.SessionId, stopwatch.ElapsedMilliseconds);
        }
    }
}
