using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social.ContentStudio;
using SocialStudio.Application.Features.Social.ContentStudio.Operations;
using SocialStudio.Domain.Social.ContentStudio;
using SocialStudio.Infrastructure.Social.ContentGeneration;

namespace SocialStudio.ApiService.BackgroundServices;

/// <summary>
/// Rebuilds the flattened preview of Content Studio items after they are saved.
///
/// <para>
/// The save request persists the authored markup and returns immediately; this worker does the
/// expensive part — one headless-Chromium render per envelope — off the request thread. See
/// <see cref="IVisualFlattenQueue"/> for why that split exists.
/// </para>
///
/// <para>
/// Every failure is swallowed and logged. A stale preview degrades the PPTX export to its legacy
/// background + text-box path; it must never take the API down or block the queue behind one bad
/// item.
/// </para>
/// </summary>
internal sealed class VisualFlattenBackgroundService : BackgroundService
{
    private readonly VisualFlattenQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VisualFlattenBackgroundService> _logger;

    public VisualFlattenBackgroundService(
        VisualFlattenQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<VisualFlattenBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await FlattenOneAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Visual flatten: item {ItemId} (org {OrgId}) could not be flattened. Its preview stays stale until the next save.",
                    request.ItemId, request.OrganizationId);
            }
        }
    }

    private async Task FlattenOneAsync(VisualFlattenRequest request, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var items = sp.GetRequiredService<IContentItemRepository>();
        var flattener = sp.GetRequiredService<VisualFlattener>();
        var clock = sp.GetRequiredService<IClock>();

        if (!flattener.CanFlatten)
        {
            return;
        }

        var item = await items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return;
        }

        // A reviewer may have moved the item on while this pass was queued. `EditVisual` drives the
        // item back to Edited, and Approved -> Edited is a LEGAL transition, so writing a refreshed
        // preview would silently un-approve someone's approved piece. A stale preview is much
        // cheaper than that, so only refresh an item still sitting in Edited.
        if (item.State != ReviewState.Edited)
        {
            return;
        }

        // Re-read rather than trusting the enqueued snapshot: several saves may have landed since,
        // and only the newest markup is worth rendering.
        var markup = item.Draft.VisualSpecMarkup;
        if (string.IsNullOrWhiteSpace(markup))
        {
            return;
        }

        var flattened = await flattener.FlattenAsync(markup, ct);

        // Unchanged means nothing rendered (no envelopes, or every render failed) — writing it back
        // would bump the item's timestamp and re-enter Edited state for no gain.
        if (flattened is null || string.Equals(flattened, markup, StringComparison.Ordinal))
        {
            return;
        }

        var result = item.EditVisual(flattened, item.Draft.Assets, clock.UtcNow);
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Visual flatten: item {ItemId} rejected the flattened markup ({Code}).",
                request.ItemId, result.Error.Code);
            return;
        }

        await items.UpdateAsync(item, ct);

        _logger.LogInformation(
            "Visual flatten: refreshed preview for item {ItemId} (org {OrgId}).",
            request.ItemId, request.OrganizationId);
    }
}
