using SocialStudio.Application.Abstractions.Social.Publications;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Publications;
using MediatR;

namespace SocialStudio.Application.Features.Social.Publications.GetPublication;

public sealed record GetPublicationQuery(Guid OrganizationId, Guid PublicationId)
    : IRequest<Result<PublicationResponse>>;

public sealed class GetPublicationQueryHandler
    : IRequestHandler<GetPublicationQuery, Result<PublicationResponse>>
{
    private readonly IPublicationRepository _publications;

    public GetPublicationQueryHandler(IPublicationRepository publications)
    {
        _publications = publications;
    }

    public async Task<Result<PublicationResponse>> Handle(GetPublicationQuery request, CancellationToken ct)
    {
        var publication = await _publications.GetByIdAsync(
            request.OrganizationId, new PublicationId(request.PublicationId), ct);

        return publication is null
            ? Result.Failure<PublicationResponse>(PublicationErrors.NotFound)
            : Result.Success(PublicationResponse.FromDomain(publication));
    }
}
