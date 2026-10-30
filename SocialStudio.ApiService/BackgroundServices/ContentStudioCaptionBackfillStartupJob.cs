using SocialStudio.Infrastructure.Persistence.Postgres.Maintenance;

namespace SocialStudio.ApiService.BackgroundServices;

/// <summary>
/// One-time, run-once-at-startup job that backfills <c>draft_caption</c> from <c>draft_text</c> for
/// pre-existing Content Studio visual items (Decision E4, task 3.6 — Content Studio editing
/// completeness). Idempotent: once every visual item has a caption the pass is a no-op, so it is
/// safe to leave registered across deployments. A failure here is logged but never crashes startup —
/// mirrors <see cref="LegacyPackageReconstructionStartupJob"/>.
/// </summary>
internal sealed class ContentStudioCaptionBackfillStartupJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ContentStudioCaptionBackfillStartupJob> _logger;

    public ContentStudioCaptionBackfillStartupJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ContentStudioCaptionBackfillStartupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var backfill = scope.ServiceProvider.GetRequiredService<ContentStudioCaptionBackfill>();

            var updated = await backfill.RunAsync(stoppingToken);

            if (updated == 0)
            {
                _logger.LogInformation("Content Studio caption backfill: nothing to do — all visual items already have a caption.");
            }
            else
            {
                _logger.LogInformation("Content Studio caption backfill ran: {Updated} item(s) backfilled.", updated);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down before the pass finished — expected.
        }
        catch (Exception ex)
        {
            // Never block application startup on a backfill failure; it will retry next boot.
            _logger.LogError(ex, "Content Studio caption backfill failed; will retry on next startup.");
        }
    }
}
