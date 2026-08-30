using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Queries;

public sealed record ContentItemAssetResponse(Stream Content, string Mime, long SizeBytes) : IDisposable
{
    public void Dispose() => Content.Dispose();
}

public sealed record GetContentItemAssetQuery(
    Guid OrganizationId,
    Guid ItemId,
    int AssetIndex) : IRequest<Result<ContentItemAssetResponse>>;

public sealed class GetContentItemAssetQueryHandler
    : IRequestHandler<GetContentItemAssetQuery, Result<ContentItemAssetResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IGeneratedAssetStorage _assetStorage;

    public GetContentItemAssetQueryHandler(
        IContentItemRepository items,
        IGeneratedAssetStorage assetStorage)
    {
        _items = items;
        _assetStorage = assetStorage;
    }

    public async Task<Result<ContentItemAssetResponse>> Handle(GetContentItemAssetQuery request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return Result.Failure<ContentItemAssetResponse>(ContentStudioErrors.ItemNotFound);
        }

        if (request.AssetIndex < 0 || request.AssetIndex >= item.Draft.Assets.Count)
        {
            return Result.Failure<ContentItemAssetResponse>(StorageErrors.NotFound);
        }

        var asset = item.Draft.Assets[request.AssetIndex];
        var opened = await _assetStorage.OpenAsync(asset.StorageUri, ct);
        if (opened.IsFailure)
        {
            return Result.Failure<ContentItemAssetResponse>(opened.Error);
        }

        return Result.Success(new ContentItemAssetResponse(
            opened.Value.Content,
            opened.Value.Mime,
            opened.Value.SizeBytes));
    }
}
