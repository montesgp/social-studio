using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Operations;

public sealed record EditItemTextCommand(Guid OrganizationId, Guid ItemId, string Text)
    : IRequest<Result<ContentItemResponse>>;

public sealed class EditItemTextCommandHandler
    : IRequestHandler<EditItemTextCommand, Result<ContentItemResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IClock _clock;

    public EditItemTextCommandHandler(IContentItemRepository items, IClock clock)
    {
        _items = items;
        _clock = clock;
    }

    public async Task<Result<ContentItemResponse>> Handle(EditItemTextCommand request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return Result.Failure<ContentItemResponse>(ContentStudioErrors.ItemNotFound);
        }

        var result = item.EditText(request.Text, MaxTextLengthFor(item), _clock.UtcNow);
        if (result.IsFailure)
        {
            return Result.Failure<ContentItemResponse>(result.Error);
        }

        await _items.UpdateAsync(item, ct);
        return Result.Success(ContentItemResponse.FromDomain(item));
    }

    // Scripts carry a longer body than a caption; captions follow the channel limit.
    private static int MaxTextLengthFor(ContentItem item)
    {
        if (item.Origin.FormatKey.Contains("script", StringComparison.OrdinalIgnoreCase))
        {
            return 10_000;
        }

        return item.Draft.TargetChannel is { Network: "youtube", Placement: "short" } ? 100 : 2_200;
    }
}
