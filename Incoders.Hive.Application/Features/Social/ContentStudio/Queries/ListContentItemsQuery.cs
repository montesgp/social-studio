using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Queries;

/// <summary>Lists the items in a set, ordered by the set's member sort order. Optional state filter.</summary>
public sealed record ListContentItemsQuery(
    Guid OrganizationId,
    Guid SetId,
    string? State) : IRequest<Result<IReadOnlyList<ContentItemResponse>>>;

public sealed class ListContentItemsQueryHandler
    : IRequestHandler<ListContentItemsQuery, Result<IReadOnlyList<ContentItemResponse>>>
{
    private readonly IContentItemRepository _items;

    public ListContentItemsQueryHandler(IContentItemRepository items)
    {
        _items = items;
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

        IReadOnlyList<ContentItemResponse> response = filtered.Select(ContentItemResponse.FromDomain).ToList();
        return Result.Success(response);
    }
}
