using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientOwnAccounts;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Networks;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientOwnAccounts.Update;

public sealed class UpdateClientOwnAccountHandler
    : IRequestHandler<UpdateClientOwnAccountCommand, Result<ClientOwnAccountResponse>>
{
    private readonly IClientOwnAccountRepository _accounts;
    private readonly ISocialNetworkRepository _networks;
    private readonly IClock _clock;

    public UpdateClientOwnAccountHandler(
        IClientOwnAccountRepository accounts,
        ISocialNetworkRepository networks,
        IClock clock)
    {
        _accounts = accounts;
        _networks = networks;
        _clock = clock;
    }

    public async Task<Result<ClientOwnAccountResponse>> Handle(
        UpdateClientOwnAccountCommand request,
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

        var newNetworkId = request.SocialNetworkId.HasValue
            ? new SocialNetworkId(request.SocialNetworkId.Value)
            : account.SocialNetworkId;

        var network = await _networks.GetByIdAsync(newNetworkId, cancellationToken);
        if (network is null)
        {
            return Result.Failure<ClientOwnAccountResponse>(ClientOwnAccountErrors.NotFound);
        }

        if (!network.IsActive)
        {
            return Result.Failure<ClientOwnAccountResponse>(ClientOwnAccountErrors.SocialNetworkInactive);
        }

        var newProfileUrl = request.ProfileUrl?.Trim() ?? string.Empty;
        if (!string.Equals(newProfileUrl, account.ProfileUrl, StringComparison.Ordinal) ||
            newNetworkId != account.SocialNetworkId)
        {
            var exists = await _accounts.ExistsByProfileUrlAsync(
                account.ClientId,
                newNetworkId,
                newProfileUrl,
                id,
                cancellationToken);
            if (exists)
            {
                return Result.Failure<ClientOwnAccountResponse>(
                    ClientOwnAccountErrors.ProfileUrlAlreadyExists(newProfileUrl));
            }
        }

        var updated = account.Update(newNetworkId, newProfileUrl, request.Handle, request.Label, request.Notes, _clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result.Failure<ClientOwnAccountResponse>(updated.Error);
        }

        _accounts.Update(account);
        await _accounts.SaveChangesAsync(cancellationToken);

        return Result.Success(ClientOwnAccountResponse.FromDomain(account, network));
    }
}
