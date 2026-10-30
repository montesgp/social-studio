using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientReferentAccounts;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Networks;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientReferentAccounts.Update;

public sealed class UpdateClientReferentAccountHandler
    : IRequestHandler<UpdateClientReferentAccountCommand, Result<ClientReferentAccountResponse>>
{
    private readonly IClientReferentAccountRepository _accounts;
    private readonly ISocialNetworkRepository _networks;
    private readonly IClock _clock;

    public UpdateClientReferentAccountHandler(
        IClientReferentAccountRepository accounts,
        ISocialNetworkRepository networks,
        IClock clock)
    {
        _accounts = accounts;
        _networks = networks;
        _clock = clock;
    }

    public async Task<Result<ClientReferentAccountResponse>> Handle(
        UpdateClientReferentAccountCommand request,
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

        var newNetworkId = request.SocialNetworkId.HasValue
            ? new SocialNetworkId(request.SocialNetworkId.Value)
            : account.SocialNetworkId;

        var network = await _networks.GetByIdAsync(newNetworkId, cancellationToken);
        if (network is null)
        {
            return Result.Failure<ClientReferentAccountResponse>(ClientReferentAccountErrors.NotFound);
        }

        if (!network.IsActive)
        {
            return Result.Failure<ClientReferentAccountResponse>(ClientReferentAccountErrors.SocialNetworkInactive);
        }

        // Re-check uniqueness only if profileUrl or network actually changes
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
                return Result.Failure<ClientReferentAccountResponse>(
                    ClientReferentAccountErrors.ProfileUrlAlreadyExists(newProfileUrl));
            }
        }

        var updated = account.Update(newNetworkId, newProfileUrl, request.Handle, request.Label, request.Notes, _clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result.Failure<ClientReferentAccountResponse>(updated.Error);
        }

        _accounts.Update(account);
        await _accounts.SaveChangesAsync(cancellationToken);

        return Result.Success(ClientReferentAccountResponse.FromDomain(account, network));
    }
}
