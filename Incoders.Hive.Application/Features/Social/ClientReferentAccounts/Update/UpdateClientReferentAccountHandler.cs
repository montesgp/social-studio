using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientReferentAccounts;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Update;

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

        // Re-check uniqueness only if profileUrl actually changes
        var newProfileUrl = request.ProfileUrl?.Trim() ?? string.Empty;
        if (!string.Equals(newProfileUrl, account.ProfileUrl, StringComparison.Ordinal))
        {
            var exists = await _accounts.ExistsByProfileUrlAsync(
                account.ClientId,
                account.SocialNetworkId,
                newProfileUrl,
                id,
                cancellationToken);
            if (exists)
            {
                return Result.Failure<ClientReferentAccountResponse>(
                    ClientReferentAccountErrors.ProfileUrlAlreadyExists(newProfileUrl));
            }
        }

        var updated = account.Update(newProfileUrl, request.Handle, request.Label, request.Notes, _clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result.Failure<ClientReferentAccountResponse>(updated.Error);
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
