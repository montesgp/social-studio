using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientOwnAccounts;
using SocialStudio.Domain.Social.Clients;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientOwnAccounts.Activate;

public sealed class ActivateClientOwnAccountHandler
    : IRequestHandler<ActivateClientOwnAccountCommand, Result<ClientOwnAccountResponse>>
{
    private readonly IClientOwnAccountRepository _accounts;
    private readonly ISocialNetworkRepository _networks;
    private readonly IClock _clock;

    public ActivateClientOwnAccountHandler(
        IClientOwnAccountRepository accounts,
        ISocialNetworkRepository networks,
        IClock clock)
    {
        _accounts = accounts;
        _networks = networks;
        _clock = clock;
    }

    public async Task<Result<ClientOwnAccountResponse>> Handle(
        ActivateClientOwnAccountCommand request,
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

        var activated = account.Activate(_clock.UtcNow);
        if (activated.IsFailure)
        {
            return Result.Failure<ClientOwnAccountResponse>(activated.Error);
        }

        _accounts.Update(account);
        await _accounts.SaveChangesAsync(cancellationToken);

        var network = await _networks.GetByIdAsync(account.SocialNetworkId, cancellationToken);
        if (network is null)
        {
            return Result.Failure<ClientOwnAccountResponse>(ClientOwnAccountErrors.NotFound);
        }

        return Result.Success(ClientOwnAccountResponse.FromDomain(account, network));
    }
}
