using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientReferentAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientReferentAccounts.GetById;

public sealed class GetClientReferentAccountByIdHandler
    : IRequestHandler<GetClientReferentAccountByIdQuery, Result<ClientReferentAccountResponse>>
{
    private readonly IClientReferentAccountRepository _accounts;
    private readonly ISocialNetworkRepository _networks;

    public GetClientReferentAccountByIdHandler(
        IClientReferentAccountRepository accounts,
        ISocialNetworkRepository networks)
    {
        _accounts = accounts;
        _networks = networks;
    }

    public async Task<Result<ClientReferentAccountResponse>> Handle(
        GetClientReferentAccountByIdQuery request,
        CancellationToken cancellationToken)
    {
        var id = new ClientReferentAccountId(request.Id);
        var account = await _accounts.GetByIdAsync(id, request.OrganizationId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<ClientReferentAccountResponse>(ClientReferentAccountErrors.NotFound);
        }

        // Resolution 1: defensive ClientId check — prevents cross-client id-leak
        if (account.ClientId != new ClientId(request.ClientId))
        {
            return Result.Failure<ClientReferentAccountResponse>(ClientReferentAccountErrors.NotFound);
        }

        var network = await _networks.GetByIdAsync(account.SocialNetworkId, cancellationToken);
        if (network is null)
        {
            return Result.Failure<ClientReferentAccountResponse>(ClientReferentAccountErrors.NotFound);
        }

        return Result.Success(ClientReferentAccountResponse.FromDomain(account, network));
    }
}
