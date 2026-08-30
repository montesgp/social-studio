using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Application.Features.Social.ContentStudio.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Queries;

/// <summary>
/// Loads an explicit, possibly CROSS-SET selection of items, preserving the requested order.
/// </summary>
/// <remarks>
/// Content Studio's other entry points are set-centric (a generation run's items). Reviewing a
/// selection triaged in the Content Bank is not: the operator picks pieces that may come from
/// different sets and origins, and there is no single set that contains exactly those. Ids missing
/// from the result are simply absent — a selection that includes a piece someone else deleted still
/// opens with the rest.
/// </remarks>
public sealed record ListContentItemsByIdsQuery(
    Guid OrganizationId,
    IReadOnlyList<Guid> ItemIds) : IRequest<Result<IReadOnlyList<ContentItemResponse>>>;

public sealed class ListContentItemsByIdsQueryHandler
    : IRequestHandler<ListContentItemsByIdsQuery, Result<IReadOnlyList<ContentItemResponse>>>
{
    /// <summary>Bounds the selection so a hand-crafted request cannot ask for an unbounded load.</summary>
    public const int MaxItems = 200;

    private readonly IContentItemRepository _items;
    private readonly IGeneratedAssetStorage? _assetStorage;

    public ListContentItemsByIdsQueryHandler(
        IContentItemRepository items, IGeneratedAssetStorage? assetStorage = null)
    {
        _items = items;
        _assetStorage = assetStorage;
    }

    public async Task<Result<IReadOnlyList<ContentItemResponse>>> Handle(
        ListContentItemsByIdsQuery request, CancellationToken ct)
    {
        var requested = request.ItemIds.Distinct().ToList();
        if (requested.Count == 0)
        {
            return Result.Failure<IReadOnlyList<ContentItemResponse>>(
                ContentStudioErrors.Validation("At least one item id is required."));
        }

        if (requested.Count > MaxItems)
        {
            return Result.Failure<IReadOnlyList<ContentItemResponse>>(
                ContentStudioErrors.Validation($"At most {MaxItems} items can be opened at once."));
        }

        var rows = await _items.ListByIdsAsync(request.OrganizationId, requested, ct);

        // Preserve the caller's order so the Studio rail matches the order the operator selected in.
        var byId = rows.ToDictionary(i => i.Id.Value);
        var ordered = requested
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToList();

        var resolved = new List<ContentItemResponse>(ordered.Count);
        foreach (var item in ordered)
        {
            var mapped = ContentItemResponse.FromDomain(item);

            // Same background-ref inlining the set-scoped listing applies, so a piece looks identical
            // whichever entry point loaded it.
            var resolvedMarkup = await StudioVisualMarkupResolver.ResolveBackgroundRefsAsync(
                mapped.VisualSpecMarkup, _assetStorage, ct);
            resolved.Add(mapped with { VisualSpecMarkup = resolvedMarkup });
        }

        IReadOnlyList<ContentItemResponse> response = resolved;
        return Result.Success(response);
    }
}
