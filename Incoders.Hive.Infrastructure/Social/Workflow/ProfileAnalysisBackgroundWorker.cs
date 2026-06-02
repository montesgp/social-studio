using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Infrastructure.Social.Workflow;

/// <summary>
/// Consumes queued profile-analysis jobs and runs the heavy scrape + AI pipeline in its OWN scope,
/// decoupled from any HTTP request (a client disconnect never cancels the work).
/// Concurrency is capped at <c>MaxConcurrency</c> simultaneous jobs.
/// A failing job must NOT crash the worker loop — unhandled exceptions are caught, logged, and
/// the worker proceeds to the next job (PA-14, PA-23).
/// Mirrors <see cref="SingleAnalysisBackgroundWorker"/> architecture with profile-specific types.
///
/// INCIDENT NOTE (obs #518): The processor MUST create a fresh DI scope per job.
/// Using a single scope across jobs causes the DbContext to be poisoned by a failed transaction,
/// breaking all subsequent jobs in that scope. <see cref="IServiceScopeFactory.CreateScope"/>
/// inside the job handler ensures each job gets a clean context.
/// </summary>
public sealed class ProfileAnalysisBackgroundWorker : BackgroundService
{
    private const int MaxConcurrency = 3;

    private readonly IProfileAnalysisJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProfileAnalysisBackgroundWorker> _logger;

    public ProfileAnalysisBackgroundWorker(
        IProfileAnalysisJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ProfileAnalysisBackgroundWorker> logger)
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

    private async ValueTask ProcessAsync(ProfileAnalysisJob job, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation(
            "Profile Analyzer worker: PROCESSING job {AnalysisId} (org {OrganizationId}).",
            job.AnalysisId,
            job.OrganizationId);

        try
        {
            // Fresh scope per job — prevents DbContext poisoning on failure (obs #518 incident note).
            using var scope = _scopeFactory.CreateScope();
            var analyzer = scope.ServiceProvider.GetRequiredService<IProfileAnalyzer>();
            await analyzer.RunQueuedAsync(job.AnalysisId, job.OrganizationId, cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation(
                "Profile Analyzer worker: FINISHED job {AnalysisId} (org {OrganizationId}) in {ElapsedMs} ms.",
                job.AnalysisId,
                job.OrganizationId,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            // RunQueuedAsync persists Failed on its own errors; this guards the worker loop itself.
            // A failing job MUST NOT crash the worker — the loop continues to the next job.
            _logger.LogError(
                ex,
                "Profile Analyzer worker: UNHANDLED EXCEPTION on job {AnalysisId} (org {OrganizationId}) after {ElapsedMs} ms. " +
                "ExceptionType={ExceptionType} Message={Message} InnerType={InnerType} InnerMessage={InnerMessage}",
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
