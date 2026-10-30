using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientReferentAccounts;
using SocialStudio.Domain.Social.Clients;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientReferentAccounts.Deactivate;

public sealed class DeactivateClientReferentAccountHandler
    : IRequestHandler<DeactivateClientReferentAccountCommand, Result<ClientReferentAccountResponse>>
{
    private readonly IClientReferentAccountRepository _accounts;
    private readonly ISocialNetworkRepository _networks;
    private readonly IClock _clock;

    public DeactivateClientReferentAccountHandler(
        IClientReferentAccountRepository accounts,
        ISocialNetworkRepository networks,
        IClock clock)
    {
        _accounts = accounts;
        _networks = networks;
        _clock = clock;
    }

    public async Task<Result<ClientReferentAccountResponse>> Handle(
        DeactivateClientReferentAccountCommand request,
        CancellationToken cancellationToken)
    {
        var id = new ClientReferentAccountId(request.Id);
        var account = await _accounts.GetByIdAsync(id, request.OrganizationId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<ClientReferentAccountResponse>(ClientReferentAccountErrors.NotFound);
        }

        // Resolution 1: defensive ClientId check
        if (account.ClientId != new ClientId(request.ClientId))
        {
            return Result.Failure<ClientReferentAccountResponse>(ClientReferentAccountErrors.NotFound);
        }

        var deactivated = account.Deactivate(_clock.UtcNow);
        if (deactivated.IsFailure)
        {
            return Result.Failure<ClientReferentAccountResponse>(deactivated.Error);
        }

        _accounts.Update(account);
        await _accounts.SaveChangesAsync(cancellationToken);

        var network = await _networks.GetByIdAsync(account.SocialNetworkId, cancellationToken);
        if (network is null)
        {
            return Result.Failure<ClientReferentAccountResponse>(ClientReferentAccountErrors.NotFound);
        }

        return Result.Success(ClientReferentAccountResponse.FromDomain(account, network));
    }
}
