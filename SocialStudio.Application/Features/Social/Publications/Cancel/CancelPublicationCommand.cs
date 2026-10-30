using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social.Publications;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Publications;
using MediatR;

namespace SocialStudio.Application.Features.Social.Publications.Cancel;

/// <summary><see cref="PublicationStatus.Scheduled"/> | <see cref="PublicationStatus.PublishFailed"/> →
/// <see cref="PublicationStatus.Cancelled"/> (design §6.3).</summary>
public sealed record CancelPublicationCommand(Guid OrganizationId, Guid PublicationId)
    : IRequest<Result<PublicationResponse>>;

public sealed class CancelPublicationCommandHandler
    : IRequestHandler<CancelPublicationCommand, Result<PublicationResponse>>
{
    private readonly IPublicationRepository _publications;
    private readonly IClock _clock;

    public CancelPublicationCommandHandler(IPublicationRepository publications, IClock clock)
    {
        _publications = publications;
        _clock = clock;
    }

    public async Task<Result<PublicationResponse>> Handle(CancelPublicationCommand request, CancellationToken ct)
    {
        var publication = await _publications.GetByIdAsync(
            request.OrganizationId, new PublicationId(request.PublicationId), ct);
        if (publication is null)
        {
            return Result.Failure<PublicationResponse>(PublicationErrors.NotFound);
        }

        var cancel = publication.Cancel(_clock.UtcNow);
        if (cancel.IsFailure)
        {
            return Result.Failure<PublicationResponse>(cancel.Error);
        }

        await _publications.UpdateAsync(publication, ct);
        return Result.Success(PublicationResponse.FromDomain(publication));
    }
}
