using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social.ContentStudio;
using SocialStudio.Application.Abstractions.Social.Publications;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentStudio;
using SocialStudio.Domain.Social.Publications;
using MediatR;

namespace SocialStudio.Application.Features.Social.Publications.Retry;

/// <summary>
/// Manual retry of a <see cref="PublicationStatus.PublishFailed"/> publication (design §6.3, OQ-5).
/// Opens attempt #N+1, persists (persist-before-call), re-runs the same connector flow as
/// publish-now, and records the outcome.
/// </summary>
public sealed record RetryPublicationCommand(Guid OrganizationId, Guid PublicationId)
    : IRequest<Result<PublicationResponse>>;

public sealed class RetryPublicationCommandHandler
    : IRequestHandler<RetryPublicationCommand, Result<PublicationResponse>>
{
    private readonly IPublicationRepository _publications;
    private readonly IContentItemRepository _items;
    private readonly PublicationPublishAttemptRunner _runner;
    private readonly IClock _clock;

    public RetryPublicationCommandHandler(
        IPublicationRepository publications,
        IContentItemRepository items,
        PublicationPublishAttemptRunner runner,
        IClock clock)
    {
        _publications = publications;
        _items = items;
        _runner = runner;
        _clock = clock;
    }

    public async Task<Result<PublicationResponse>> Handle(RetryPublicationCommand request, CancellationToken ct)
    {
        var publication = await _publications.GetByIdAsync(
            request.OrganizationId, new PublicationId(request.PublicationId), ct);
        if (publication is null)
        {
            return Result.Failure<PublicationResponse>(PublicationErrors.NotFound);
        }

        // Retry() only opens a new attempt — the underlying BeginPublishing() transition also accepts
        // Scheduled -> Publishing (the future dispatcher's due-fire path), so this handler enforces the
        // PublishFailed-only precondition itself (design §6.3, tasks.md 9.3).
        if (publication.Status != PublicationStatus.PublishFailed)
        {
            return Result.Failure<PublicationResponse>(PublicationErrors.IllegalPublicationTransition);
        }

        var retry = publication.Retry(_clock.UtcNow);
        if (retry.IsFailure)
        {
            return Result.Failure<PublicationResponse>(retry.Error);
        }

        // Persist-before-call, same discipline as publish-now.
        await _publications.UpdateAsync(publication, ct);

        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(publication.ContentItemId), ct);
        if (item is null)
        {
            publication.MarkFailed("The source content item could not be found for retry.", _clock.UtcNow);
            await _publications.UpdateAsync(publication, ct);
            return Result.Success(PublicationResponse.FromDomain(publication));
        }

        var outcome = await _runner.RunAsync(request.OrganizationId, item, publication.Destination, ct);

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
