using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Abstractions.Social.ContentStudio;
using SocialStudio.Application.Abstractions.Social.Publications;
using SocialStudio.Application.Features.Social.ContentStudio;
using SocialStudio.Application.Features.Social.ContentStudio.Shared;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentStudio;
using MediatR;

namespace SocialStudio.Application.Features.Social.Publications.ListClientContentBank;

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

        // "Open in Studio": resolve each item's OWNING set through set membership, in one batched
        // query. This used to look the set up by the item's Origin.SourceRunId, but that is the
        // generation JOB id while a GenerationRun set is keyed by a run id derived from
        // (analysisId, clientId) — they never match, so every item came back with a null set id and
        // the client fell through to a re-materialize path that could not succeed.
        var setIdByItemId = _sets is null || itemIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await _sets.GetSetIdsByItemIdsAsync(request.OrganizationId, itemIds, ct);

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

            var setId = setIdByItemId.TryGetValue(item.Id.Value, out var owningSetId)
                ? owningSetId
                : (Guid?)null;

            response.Add(ContentBankItemResponse.FromMapped(
                mapped with { VisualSpecMarkup = resolvedMarkup }, itemPublications, setId));
        }

        IReadOnlyList<ContentBankItemResponse> result = response;
        return Result.Success(result);
    }

}
