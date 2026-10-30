using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Abstractions.Social.ContentStudio;
using SocialStudio.Application.Features.Social.ContentStudio.Shared;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentStudio;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentStudio.Queries;

/// <summary>Lists the items in a set, ordered by the set's member sort order. Optional state filter.</summary>
public sealed record ListContentItemsQuery(
    Guid OrganizationId,
    Guid SetId,
    string? State) : IRequest<Result<IReadOnlyList<ContentItemResponse>>>;

public sealed class ListContentItemsQueryHandler
    : IRequestHandler<ListContentItemsQuery, Result<IReadOnlyList<ContentItemResponse>>>
{
    private readonly IContentItemRepository _items;
    private readonly IGeneratedAssetStorage? _assetStorage;

    public ListContentItemsQueryHandler(IContentItemRepository items, IGeneratedAssetStorage? assetStorage = null)
    {
        _items = items;
        _assetStorage = assetStorage;
    }

    public async Task<Result<IReadOnlyList<ContentItemResponse>>> Handle(ListContentItemsQuery request, CancellationToken ct)
    {
        var rows = await _items.ListBySetAsync(request.OrganizationId, new ContentSetId(request.SetId), ct);

        IEnumerable<ContentItem> filtered = rows;
        if (!string.IsNullOrWhiteSpace(request.State)
            && Enum.TryParse<ReviewState>(request.State, ignoreCase: true, out var state))
        {
            filtered = rows.Where(i => i.State == state);
        }

        var mapped = filtered.Select(ContentItemResponse.FromDomain).ToList();

        // Issue #58 (LEAN fix, read-only): inline any storage-path background reference in each
        // item's returned markup as a data-URI so the studio list/package preview renders it again.
        var resolved = new List<ContentItemResponse>(mapped.Count);
        foreach (var item in mapped)
        {
            var resolvedMarkup = await StudioVisualMarkupResolver.ResolveBackgroundRefsAsync(
                item.VisualSpecMarkup, _assetStorage, ct);
            resolved.Add(item with { VisualSpecMarkup = resolvedMarkup });
        }

        IReadOnlyList<ContentItemResponse> response = resolved;
        return Result.Success(response);
    }
}
