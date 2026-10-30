using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Abstractions.Social.ContentStudio;
using SocialStudio.Application.Features.Social.ContentStudio.Shared;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentStudio;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentStudio.Queries;

public sealed record GetContentItemQuery(Guid OrganizationId, Guid ItemId) : IRequest<Result<ContentItemResponse>>;

public sealed class GetContentItemQueryHandler
    : IRequestHandler<GetContentItemQuery, Result<ContentItemResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IGeneratedAssetStorage? _assetStorage;

    public GetContentItemQueryHandler(IContentItemRepository items, IGeneratedAssetStorage? assetStorage = null)
    {
        _items = items;
        _assetStorage = assetStorage;
    }

    public async Task<Result<ContentItemResponse>> Handle(GetContentItemQuery request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return Result.Failure<ContentItemResponse>(ContentStudioErrors.ItemNotFound);
        }

        var response = ContentItemResponse.FromDomain(item);

        // Issue #58 (LEAN fix, read-only): inline any storage-path background reference in the
        // returned markup as a data-URI so the studio can render it again. Never persisted back.
        var resolvedMarkup = await StudioVisualMarkupResolver.ResolveBackgroundRefsAsync(
            response.VisualSpecMarkup, _assetStorage, ct);

        return Result.Success(response with { VisualSpecMarkup = resolvedMarkup });
    }
}
