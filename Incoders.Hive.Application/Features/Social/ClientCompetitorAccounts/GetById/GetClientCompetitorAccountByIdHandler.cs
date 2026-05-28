using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientCompetitorAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientCompetitorAccounts.GetById;

public sealed class GetClientCompetitorAccountByIdHandler
    : IRequestHandler<GetClientCompetitorAccountByIdQuery, Result<ClientCompetitorAccountResponse>>
{
    private readonly IClientCompetitorAccountRepository _accounts;
    private readonly ISocialNetworkRepository _networks;

    public GetClientCompetitorAccountByIdHandler(
        IClientCompetitorAccountRepository accounts,
        ISocialNetworkRepository networks)
    {
        _accounts = accounts;
        _networks = networks;
    }

    public async Task<Result<ClientCompetitorAccountResponse>> Handle(
        GetClientCompetitorAccountByIdQuery request,
        CancellationToken cancellationToken)
    {
        var id = new ClientCompetitorAccountId(request.Id);
        var account = await _accounts.GetByIdAsync(id, request.OrganizationId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<ClientCompetitorAccountResponse>(ClientCompetitorAccountErrors.NotFound);
        }

        if (account.ClientId != new ClientId(request.ClientId))
        {
            return Result.Failure<ClientCompetitorAccountResponse>(ClientCompetitorAccountErrors.NotFound);
        }

        var network = await _networks.GetByIdAsync(account.SocialNetworkId, cancellationToken);
        if (network is null)
        {
            return Result.Failure<ClientCompetitorAccountResponse>(ClientCompetitorAccountErrors.NotFound);
        }

        return Result.Success(ClientCompetitorAccountResponse.FromDomain(account, network));
    }
}
