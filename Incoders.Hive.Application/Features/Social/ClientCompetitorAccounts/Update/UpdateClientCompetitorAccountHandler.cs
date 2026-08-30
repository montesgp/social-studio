using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientCompetitorAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientCompetitorAccounts.Update;

public sealed class UpdateClientCompetitorAccountHandler
    : IRequestHandler<UpdateClientCompetitorAccountCommand, Result<ClientCompetitorAccountResponse>>
{
    private readonly IClientCompetitorAccountRepository _accounts;
    private readonly ISocialNetworkRepository _networks;
    private readonly IClock _clock;

    public UpdateClientCompetitorAccountHandler(
        IClientCompetitorAccountRepository accounts,
        ISocialNetworkRepository networks,
        IClock clock)
    {
        _accounts = accounts;
        _networks = networks;
        _clock = clock;
    }

    public async Task<Result<ClientCompetitorAccountResponse>> Handle(
        UpdateClientCompetitorAccountCommand request,
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

        var newNetworkId = request.SocialNetworkId.HasValue
            ? new SocialNetworkId(request.SocialNetworkId.Value)
            : account.SocialNetworkId;

        var network = await _networks.GetByIdAsync(newNetworkId, cancellationToken);
        if (network is null)
        {
            return Result.Failure<ClientCompetitorAccountResponse>(ClientCompetitorAccountErrors.NotFound);
        }

        if (!network.IsActive)
        {
            return Result.Failure<ClientCompetitorAccountResponse>(ClientCompetitorAccountErrors.SocialNetworkInactive);
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
                return Result.Failure<ClientCompetitorAccountResponse>(
                    ClientCompetitorAccountErrors.ProfileUrlAlreadyExists(newProfileUrl));
            }
        }

        var updated = account.Update(newNetworkId, newProfileUrl, request.Handle, request.Label, request.Notes, _clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result.Failure<ClientCompetitorAccountResponse>(updated.Error);
        }

        _accounts.Update(account);
        await _accounts.SaveChangesAsync(cancellationToken);

        return Result.Success(ClientCompetitorAccountResponse.FromDomain(account, network));
    }
}
