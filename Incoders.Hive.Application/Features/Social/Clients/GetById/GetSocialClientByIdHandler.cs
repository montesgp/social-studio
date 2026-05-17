using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.GetById;

public sealed class GetSocialClientByIdHandler
    : IRequestHandler<GetSocialClientByIdQuery, Result<SocialClientResponse>>
{
    private readonly ISocialClientRepository _clients;

    public GetSocialClientByIdHandler(ISocialClientRepository clients)
    {
        _clients = clients;
    }

    public async Task<Result<SocialClientResponse>> Handle(
        GetSocialClientByIdQuery request,
        CancellationToken cancellationToken)
    {
        var client = await _clients.GetByIdAsync(
            new ClientId(request.Id),
            request.OrganizationId,
            cancellationToken);

        return client is null
            ? Result.Failure<SocialClientResponse>(ClientErrors.NotFound)
            : Result.Success(SocialClientResponse.FromDomain(client));
    }
}
