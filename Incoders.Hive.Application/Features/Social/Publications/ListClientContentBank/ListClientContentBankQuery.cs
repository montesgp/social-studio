using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Application.Features.Social.ContentStudio;
using Incoders.Hive.Application.Features.Social.ContentStudio.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Publications.ListClientContentBank;

/// <summary>
/// Client-wide Content Bank listing (design §5.2, CB-01..CB-04). Cross-set, cross-origin — every
/// <see cref="ContentItem"/> for the client, optionally filtered by created-at range and/or review
/// state, joined with a lightweight per-item publication summary.
/// </summary>
public sealed record ListClientContentBankQuery(
    Guid OrganizationId,
    Guid ClientId,
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? State) : IRequest<Result<IReadOnlyList<ContentBankItemResponse>>>;

public sealed class ListClientContentBankQueryHandler
    : IRequestHandler<ListClientContentBankQuery, Result<IReadOnlyList<ContentBankItemResponse>>>
{
    private readonly IContentItemRepository _items;
    private readonly IPublicationRepository _publications;
    private readonly IGeneratedAssetStorage? _assetStorage;
    private readonly IContentSetRepository? _sets;

    public ListClientContentBankQueryHandler(
        IContentItemRepository items,
        IPublicationRepository publications,
        IGeneratedAssetStorage? assetStorage = null,
        IContentSetRepository? sets = null)
    {
        _items = items;
        _publications = publications;
        _assetStorage = assetStorage;
        _sets = sets;
    }

    public async Task<Result<IReadOnlyList<ContentBankItemResponse>>> Handle(
        ListClientContentBankQuery request, CancellationToken ct)
    {
        ReviewState? state = null;
        if (!string.IsNullOrWhiteSpace(request.State) && Enum.TryParse<ReviewState>(request.State, ignoreCase: true, out var parsed))
        {
            state = parsed;
        }

        var filter = new ContentBankFilter(request.FromUtc, request.ToUtc, state);
        var rows = await _items.ListByClientAsync(request.OrganizationId, request.ClientId, filter, ct);

        var itemIds = rows.Select(i => i.Id.Value).ToList();
        var publications = itemIds.Count == 0
            ? []
            : await _publications.ListByContentItemsAsync(request.OrganizationId, itemIds, ct);

        var publicationsByItem = publications
            .GroupBy(p => p.ContentItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PublicationSummaryDto>)g.Select(PublicationSummaryDto.FromDomain).ToList());

        // "Open in Studio" fix: resolve each item's owning set id once per distinct source-run id
        // (memoized — a client's Bank listing routinely has many items sharing the same run/set).
        var setIdByRunId = new Dictionary<Guid, Guid?>();

        var response = new List<ContentBankItemResponse>(rows.Count);
        foreach (var item in rows)
        {
            var mapped = ContentItemResponse.FromDomain(item);

            // Issue #58 (LEAN fix, read-only) — same background-ref inlining the Studio list applies.
            var resolvedMarkup = await StudioVisualMarkupResolver.ResolveBackgroundRefsAsync(
                mapped.VisualSpecMarkup, _assetStorage, ct);

            var itemPublications = publicationsByItem.TryGetValue(item.Id.Value, out var list)
                ? list
                : (IReadOnlyList<PublicationSummaryDto>)[];

            var setId = await ResolveSetIdAsync(request.OrganizationId, item.Origin.SourceRunId, setIdByRunId, ct);

            response.Add(ContentBankItemResponse.FromMapped(
                mapped with { VisualSpecMarkup = resolvedMarkup }, itemPublications, setId));
        }

        IReadOnlyList<ContentBankItemResponse> result = response;
        return Result.Success(result);
    }

    private async Task<Guid?> ResolveSetIdAsync(
        Guid organizationId, Guid sourceRunId, Dictionary<Guid, Guid?> cache, CancellationToken ct)
    {
        if (_sets is null)
        {
            return null;
        }

        if (cache.TryGetValue(sourceRunId, out var cached))
        {
            return cached;
        }

        var set = await _sets.GetByRunAsync(organizationId, sourceRunId, ct);
        var setId = set?.Id.Value;
        cache[sourceRunId] = setId;
        return setId;
    }
}
