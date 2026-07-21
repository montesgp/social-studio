using FluentValidation;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using Incoders.Hive.Domain.Social.Publications;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Publications.PublishNow;

/// <summary>
/// Publishes a Content Bank item immediately (design §6.3, §7). Loads the <see cref="ContentItem"/>
/// read-only, gates on the editorial <see cref="ReviewState"/>, creates the <see cref="Publication"/>
/// aggregate in <see cref="PublicationStatus.Publishing"/>, PERSISTS FIRST (persist-before-call), then
/// invokes the connector and records the outcome. Never mutates the source <see cref="ContentItem"/>.
/// </summary>
public sealed record PublishItemNowCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid ContentItemId,
    Guid ClientAccountConnectionId,
    Guid SocialNetworkId,
    string NetworkKey) : IRequest<Result<PublicationResponse>>;

public sealed class PublishItemNowCommandValidator : AbstractValidator<PublishItemNowCommand>
{
    public PublishItemNowCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.ContentItemId).NotEmpty();
        RuleFor(x => x.ClientAccountConnectionId).NotEmpty();
        RuleFor(x => x.SocialNetworkId).NotEmpty();
        RuleFor(x => x.NetworkKey).NotEmpty();
    }
}

public sealed class PublishItemNowCommandHandler
    : IRequestHandler<PublishItemNowCommand, Result<PublicationResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IPublicationRepository _publications;
    private readonly PublicationPublishAttemptRunner _runner;
    private readonly IClock _clock;

    public PublishItemNowCommandHandler(
        IContentItemRepository items,
        IPublicationRepository publications,
        PublicationPublishAttemptRunner runner,
        IClock clock)
    {
        _items = items;
        _publications = publications;
        _runner = runner;
        _clock = clock;
    }

    public async Task<Result<PublicationResponse>> Handle(PublishItemNowCommand request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ContentItemId), ct);
        if (item is null)
        {
            return Result.Failure<PublicationResponse>(ContentStudioErrors.ItemNotFound);
        }

        var gate = PublicationEditorialGate.EnsurePublishable(item);
        if (gate.IsFailure)
        {
            return Result.Failure<PublicationResponse>(gate.Error);
        }

        var destination = new PublicationDestination(
            request.ClientAccountConnectionId, request.SocialNetworkId, request.NetworkKey);

        var created = Publication.PublishNow(
            PublicationId.New(), request.OrganizationId, request.ClientId, request.ContentItemId,
            destination, _clock.UtcNow);
        if (created.IsFailure)
        {
            return Result.Failure<PublicationResponse>(created.Error);
        }

        var publication = created.Value;

        // Persist-before-call (design §7): the Publishing row must exist before the connector runs,
        // so a crash mid-call leaves a recoverable (reclaimable) row instead of nothing at all.
        await _publications.AddAsync(publication, ct);

        var outcome = await _runner.RunAsync(request.OrganizationId, item, destination, ct);

        if (outcome.IsFailure)
        {
            publication.MarkFailed(outcome.Error.MessageKey, _clock.UtcNow);
        }
        else
        {
            publication.MarkPublished(outcome.Value, _clock.UtcNow);
        }

        await _publications.UpdateAsync(publication, ct);
        return Result.Success(PublicationResponse.FromDomain(publication));
    }
}
