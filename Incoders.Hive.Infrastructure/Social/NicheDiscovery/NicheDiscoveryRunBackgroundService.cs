using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// Consumes queued discovery-run jobs and runs <see cref="NicheDiscoveryRunProcessor"/> in its OWN DI
/// scope per job (mirrors <c>ProfileAnalysisBackgroundWorker</c>, obs #518 incident note: a single
/// shared scope lets a failed transaction poison the DbContext for every subsequent job). A failing
/// job must NOT crash the worker loop — unhandled exceptions are caught, logged, and the worker moves
/// to the next job.
/// </summary>
public sealed class NicheDiscoveryRunBackgroundService : BackgroundService
{
    private readonly INicheDiscoveryRunQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NicheDiscoveryRunBackgroundService> _logger;

    public NicheDiscoveryRunBackgroundService(
        INicheDiscoveryRunQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<NicheDiscoveryRunBackgroundService>? logger = null)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger ?? NullLogger<NicheDiscoveryRunBackgroundService>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            (Guid OrganizationId, Domain.Social.NicheDiscovery.DiscoveryRunId RunId)? job;
            try
            {
                job = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (job is null)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            await ProcessAsync(job.Value.OrganizationId, job.Value.RunId, stoppingToken);
        }
    }

    private async Task ProcessAsync(
        Guid organizationId, Domain.Social.NicheDiscovery.DiscoveryRunId runId, CancellationToken cancellationToken)
    {
        try
        {
            // Fresh scope per job — prevents DbContext poisoning on failure (obs #518 incident note).
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<INicheDiscoveryRunProcessor>();

            await processor.RunAsync(organizationId, runId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The processor persists Failed on its own domain errors; this guards the worker loop
            // itself against an unexpected exception escaping the processor entirely.
            _logger.LogError(
                ex,
                "NicheDiscovery run worker: UNHANDLED EXCEPTION on run {RunId} (org {OrganizationId}).",
                runId.Value,
                organizationId);
        }
    }
}
