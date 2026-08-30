using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientOwnAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientOwnAccounts.GetById;

public sealed class GetClientOwnAccountByIdHandler
    : IRequestHandler<GetClientOwnAccountByIdQuery, Result<ClientOwnAccountResponse>>
{
    private readonly IClientOwnAccountRepository _accounts;
    private readonly ISocialNetworkRepository _networks;

    public GetClientOwnAccountByIdHandler(
        IClientOwnAccountRepository accounts,
        ISocialNetworkRepository networks)
    {
        _accounts = accounts;
        _networks = networks;
    }

    public async Task<Result<ClientOwnAccountResponse>> Handle(
        GetClientOwnAccountByIdQuery request,
        CancellationToken cancellationToken)
    {
        var id = new ClientOwnAccountId(request.Id);
        var account = await _accounts.GetByIdAsync(id, request.OrganizationId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<ClientOwnAccountResponse>(ClientOwnAccountErrors.NotFound);
        }

        if (account.ClientId != new ClientId(request.ClientId))
        {
            return Result.Failure<ClientOwnAccountResponse>(ClientOwnAccountErrors.NotFound);
        }

        var network = await _networks.GetByIdAsync(account.SocialNetworkId, cancellationToken);
        if (network is null)
        {
            return Result.Failure<ClientOwnAccountResponse>(ClientOwnAccountErrors.NotFound);
        }

        return Result.Success(ClientOwnAccountResponse.FromDomain(account, network));
    }
}
