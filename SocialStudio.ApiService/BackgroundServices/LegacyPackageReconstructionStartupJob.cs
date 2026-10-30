using SocialStudio.Application.Abstractions.Social.ContentGeneration;

namespace SocialStudio.ApiService.BackgroundServices;

/// <summary>
/// One-time, run-once-at-startup job that reconstructs legacy single-analyzer packages
/// to the current format taxonomy (story → story-single, feed-post → feed-single,
/// carousel → feed-carousel). Renames the stored objects AND updates the DB rows so
/// storage and database stay consistent.
///
/// Idempotent: once everything is canonical the pass is a no-op, so it is safe to leave
/// registered across deployments. A failure here is logged but never crashes startup.
/// </summary>
internal sealed class LegacyPackageReconstructionStartupJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LegacyPackageReconstructionStartupJob> _logger;

    public LegacyPackageReconstructionStartupJob(
        IServiceScopeFactory scopeFactory,
        ILogger<LegacyPackageReconstructionStartupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var reconstructor = scope.ServiceProvider.GetRequiredService<ILegacyPackageReconstructor>();

            var report = await reconstructor.ReconstructAsync(stoppingToken);

            if (report.NoOp)
            {
                _logger.LogInformation("Legacy package reconstruction: nothing to do — all packages already canonical.");
            }
            else
            {
                _logger.LogInformation(
                    "Legacy package reconstruction ran: {ReKeyed} results re-keyed, {Renamed} assets renamed, {Stale} stale results deleted.",
                    report.ResultsReKeyed, report.AssetsRenamed, report.StaleResultsDeleted);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down before the pass finished — expected.
        }
        catch (Exception ex)
        {
            // Never block application startup on a reconstruction failure; it will retry next boot.
            _logger.LogError(ex, "Legacy package reconstruction failed; will retry on next startup.");
        }
    }
}
