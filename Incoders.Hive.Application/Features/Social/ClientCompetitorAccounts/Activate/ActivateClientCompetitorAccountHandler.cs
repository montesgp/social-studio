using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientCompetitorAccounts;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientCompetitorAccounts.Activate;

public sealed class ActivateClientCompetitorAccountHandler
    : IRequestHandler<ActivateClientCompetitorAccountCommand, Result<ClientCompetitorAccountResponse>>
{
    private readonly IClientCompetitorAccountRepository _accounts;
    private readonly ISocialNetworkRepository _networks;
    private readonly IClock _clock;

    public ActivateClientCompetitorAccountHandler(
        IClientCompetitorAccountRepository accounts,
        ISocialNetworkRepository networks,
        IClock clock)
    {
        _accounts = accounts;
        _networks = networks;
        _clock = clock;
    }

    public async Task<Result<ClientCompetitorAccountResponse>> Handle(
        ActivateClientCompetitorAccountCommand request,
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

        var activated = account.Activate(_clock.UtcNow);
        if (activated.IsFailure)
        {
            return Result.Failure<ClientCompetitorAccountResponse>(activated.Error);
        }

        _accounts.Update(account);
        await _accounts.SaveChangesAsync(cancellationToken);

        var network = await _networks.GetByIdAsync(account.SocialNetworkId, cancellationToken);
        if (network is null)
        {
            return Result.Failure<ClientCompetitorAccountResponse>(ClientCompetitorAccountErrors.NotFound);
        }

        return Result.Success(ClientCompetitorAccountResponse.FromDomain(account, network));
    }
}
