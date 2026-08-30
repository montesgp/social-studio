using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Operations;

/// <summary>
/// Edits the dedicated publication <see cref="Caption"/> field (R5.5 — Content Studio editing
/// completeness). Distinct from <see cref="EditItemTextCommand"/>: this command never touches
/// <c>Draft.Text</c> (which keeps carrying reel scripts).
/// </summary>
public sealed record EditItemCaptionCommand(Guid OrganizationId, Guid ItemId, string Caption)
    : IRequest<Result<ContentItemResponse>>;

public sealed class EditItemCaptionCommandHandler
    : IRequestHandler<EditItemCaptionCommand, Result<ContentItemResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IClock _clock;

    public EditItemCaptionCommandHandler(IContentItemRepository items, IClock clock)
    {
        _items = items;
        _clock = clock;
    }

    public async Task<Result<ContentItemResponse>> Handle(EditItemCaptionCommand request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return Result.Failure<ContentItemResponse>(ContentStudioErrors.ItemNotFound);
        }

        var result = item.EditCaption(request.Caption, _clock.UtcNow);
        if (result.IsFailure)
        {
            return Result.Failure<ContentItemResponse>(result.Error);
        }

        await _items.UpdateAsync(item, ct);
        return Result.Success(ContentItemResponse.FromDomain(item));
    }
}
