using FluentValidation;
using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social.ContentStudio;
using SocialStudio.Application.Abstractions.Social.Publications;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientAccountConnections;
using SocialStudio.Domain.Social.ContentStudio;
using SocialStudio.Domain.Social.Publications;
using MediatR;

namespace SocialStudio.Application.Features.Social.Publications.Schedule;

/// <summary>
/// Creates a <see cref="Publication"/> in <see cref="PublicationStatus.Scheduled"/> (design §6.3, OQ-2).
/// The same editorial gate as <c>PublishItemNowCommand</c> applies. No infrastructure fires
/// <c>Scheduled → Publishing</c> in v1 — this command and its endpoint exist domain-ready for the
/// future dispatcher; the v1 UI does not expose the action (design §15).
/// </summary>
public sealed record SchedulePublicationCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid ContentItemId,
    Guid ClientAccountConnectionId,
    Guid SocialNetworkId,
    string NetworkKey,
    DateTime ScheduledForUtc) : IRequest<Result<PublicationResponse>>;

public sealed class SchedulePublicationCommandValidator : AbstractValidator<SchedulePublicationCommand>
{
    public SchedulePublicationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.ContentItemId).NotEmpty();
        RuleFor(x => x.ClientAccountConnectionId).NotEmpty();
        RuleFor(x => x.SocialNetworkId).NotEmpty();
        RuleFor(x => x.NetworkKey).NotEmpty();
        RuleFor(x => x.ScheduledForUtc)
            .Must(v => v > DateTime.UtcNow)
            .WithMessage("Scheduled time must be in the future.");
    }
}

public sealed class SchedulePublicationCommandHandler
    : IRequestHandler<SchedulePublicationCommand, Result<PublicationResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IPublicationRepository _publications;
    private readonly IClientAccountConnectionRepository _connections;
    private readonly IClock _clock;

    public SchedulePublicationCommandHandler(
        IContentItemRepository items,
        IPublicationRepository publications,
        IClientAccountConnectionRepository connections,
        IClock clock)
    {
        _items = items;
        _publications = publications;
        _connections = connections;
        _clock = clock;
    }

    public async Task<Result<PublicationResponse>> Handle(SchedulePublicationCommand request, CancellationToken ct)
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

        // Ownership guard: the target connection must belong to the SAME client that owns the
        // content item — request.ClientId comes straight from the HTTP body and must never be
        // trusted for cross-client attribution (see security fix).
        var connection = await _connections.GetByIdAsync(
            request.OrganizationId, new ClientAccountConnectionId(request.ClientAccountConnectionId), ct);
        if (connection is null || connection.ClientId.Value != item.ClientId)
        {
            return Result.Failure<PublicationResponse>(PublicationErrors.DestinationUnresolved);
        }

        var destination = new PublicationDestination(
            request.ClientAccountConnectionId, request.SocialNetworkId, request.NetworkKey);

        var created = Publication.Schedule(
            PublicationId.New(), request.OrganizationId, item.ClientId, request.ContentItemId,
            destination, request.ScheduledForUtc, _clock.UtcNow);
        if (created.IsFailure)
        {
            return Result.Failure<PublicationResponse>(created.Error);
        }

        await _publications.AddAsync(created.Value, ct);
        return Result.Success(PublicationResponse.FromDomain(created.Value));
    }
}
