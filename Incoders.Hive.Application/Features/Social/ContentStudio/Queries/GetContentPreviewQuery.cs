using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Application.Features.Social.ContentStudio.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Queries;

/// <summary>Channel rendering constraints for the destination preview emulator (presentation-only).</summary>
public sealed record ChannelConstraintsDto(
    string AspectRatio,
    int MaxCaptionLength,
    int MaxSlides);

/// <summary>
/// Normalized, presentation-only projection that drives the frontend destination emulator (CS-07).
/// Reading it never mutates the item.
/// </summary>
public sealed record ContentPreviewProjection(
    Guid ItemId,
    string Medium,
    string TargetNetwork,
    string TargetPlacement,
    string? Text,
    string? VisualSpecMarkup,
    IReadOnlyList<ContentAssetDto> Assets,
    ChannelConstraintsDto Constraints);

public sealed record GetContentPreviewQuery(Guid OrganizationId, Guid ItemId) : IRequest<Result<ContentPreviewProjection>>;

public sealed class GetContentPreviewQueryHandler
    : IRequestHandler<GetContentPreviewQuery, Result<ContentPreviewProjection>>
{
    private readonly IContentItemRepository _items;
    private readonly IGeneratedAssetStorage? _assetStorage;

    public GetContentPreviewQueryHandler(IContentItemRepository items, IGeneratedAssetStorage? assetStorage = null)
    {
        _items = items;
        _assetStorage = assetStorage;
    }

    public async Task<Result<ContentPreviewProjection>> Handle(GetContentPreviewQuery request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return Result.Failure<ContentPreviewProjection>(ContentStudioErrors.ItemNotFound);
        }

        var channel = item.Draft.TargetChannel;

        // Issue #58 (LEAN fix, read-only): inline any storage-path background reference in the
        // returned markup as a data-URI so the destination emulator preview renders it again.
        var resolvedMarkup = await StudioVisualMarkupResolver.ResolveBackgroundRefsAsync(
            item.Draft.VisualSpecMarkup, _assetStorage, ct);

        var projection = new ContentPreviewProjection(
            item.Id.Value,
            item.Kind.Medium.ToString(),
            channel.Network,
            channel.Placement,
            item.Draft.Text,
            resolvedMarkup,
            item.Draft.Assets.Select(ContentAssetDto.FromDomain).ToList(),
            ConstraintsFor(channel));

        return Result.Success(projection);
    }

    private static ChannelConstraintsDto ConstraintsFor(TargetChannel channel) => (channel.Network, channel.Placement) switch
    {
        ("instagram", "feed") => new ChannelConstraintsDto("4:5", 2200, 1),
        ("instagram", "carousel") => new ChannelConstraintsDto("1:1", 2200, 10),
        ("instagram", "story") => new ChannelConstraintsDto("9:16", 0, 1),
        ("instagram", "reel") => new ChannelConstraintsDto("9:16", 2200, 1),
        ("tiktok", _) => new ChannelConstraintsDto("9:16", 2200, 1),
        ("youtube", "short") => new ChannelConstraintsDto("9:16", 100, 1),
        _ => new ChannelConstraintsDto("1:1", 2200, 1),
    };
}
