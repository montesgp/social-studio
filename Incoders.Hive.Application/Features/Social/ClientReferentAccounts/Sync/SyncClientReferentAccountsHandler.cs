using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientReferentAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Sync;

public sealed class SyncClientReferentAccountsHandler
    : IRequestHandler<SyncClientReferentAccountsCommand, Result<IReadOnlyList<ClientReferentAccountResponse>>>
{
    private readonly IClientReferentAccountRepository _accounts;
    private readonly ISocialClientRepository _clients;
    private readonly ISocialNetworkRepository _networks;
    private readonly IClock _clock;

    public SyncClientReferentAccountsHandler(
        IClientReferentAccountRepository accounts,
        ISocialClientRepository clients,
        ISocialNetworkRepository networks,
        IClock clock)
    {
        _accounts = accounts;
        _clients = clients;
        _networks = networks;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<ClientReferentAccountResponse>>> Handle(
        SyncClientReferentAccountsCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<IReadOnlyList<ClientReferentAccountResponse>>(
                ClientReferentAccountErrors.NotFound);
        }

        var incoming = request.Items
            .Select((item, index) => new IncomingAccount(
                item.Id,
                new SocialNetworkId(item.SocialNetworkId),
                item.ProfileUrl?.Trim() ?? string.Empty,
                item.Handle,
                item.Label,
                item.Notes,
                item.IsActive,
                index))
            .ToList();

        var duplicateId = incoming
            .Where(x => x.Id.HasValue)
            .GroupBy(x => x.Id!.Value)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateId is not null)
        {
            return Result.Failure<IReadOnlyList<ClientReferentAccountResponse>>(
                ClientReferentAccountErrors.Validation("The same referent account id appears more than once."));
        }

        var duplicateProfile = incoming
            .GroupBy(x => new AccountKey(x.SocialNetworkId, x.ProfileUrl))
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateProfile is not null)
        {
            return Result.Failure<IReadOnlyList<ClientReferentAccountResponse>>(
                ClientReferentAccountErrors.ProfileUrlAlreadyExists(duplicateProfile.Key.ProfileUrl));
        }

        var existing = await _accounts.GetAllByClientAsync(clientId, request.OrganizationId, cancellationToken);
        var existingById = existing.ToDictionary(x => x.Id.Value);

        var allNetworkIds = incoming
            .Select(x => x.SocialNetworkId)
            .Concat(existing.Select(x => x.SocialNetworkId))
            .Distinct()
            .ToArray();

        var networks = await _networks.GetByIdsAsync(allNetworkIds, cancellationToken);
        var networkById = networks.ToDictionary(x => x.Id);

        foreach (var item in incoming)
        {
            if (!networkById.TryGetValue(item.SocialNetworkId, out var network))
            {
                return Result.Failure<IReadOnlyList<ClientReferentAccountResponse>>(
                    ClientReferentAccountErrors.NotFound);
            }

            if (!network.IsActive)
            {
                return Result.Failure<IReadOnlyList<ClientReferentAccountResponse>>(
                    ClientReferentAccountErrors.SocialNetworkInactive);
            }
        }

        var claimed = new HashSet<Guid>();
        var resultAccounts = new List<ClientReferentAccount>();

        foreach (var item in incoming)
        {
            var targetKey = new AccountKey(item.SocialNetworkId, item.ProfileUrl);
            ClientReferentAccount? account = null;

            if (item.Id.HasValue)
            {
                if (!existingById.TryGetValue(item.Id.Value, out account))
                {
                    return Result.Failure<IReadOnlyList<ClientReferentAccountResponse>>(
                        ClientReferentAccountErrors.NotFound);
                }

                var conflicting = existing.FirstOrDefault(x =>
                    x.Id.Value != item.Id.Value &&
                    !claimed.Contains(x.Id.Value) &&
                    new AccountKey(x.SocialNetworkId, x.ProfileUrl) == targetKey);

                if (conflicting is not null)
                {
                    return Result.Failure<IReadOnlyList<ClientReferentAccountResponse>>(
                        ClientReferentAccountErrors.ProfileUrlAlreadyExists(item.ProfileUrl));
                }
            }
            else
            {
                account = existing.FirstOrDefault(x =>
                    !claimed.Contains(x.Id.Value) &&
                    new AccountKey(x.SocialNetworkId, x.ProfileUrl) == targetKey);
            }

            if (account is null)
            {
                var created = ClientReferentAccount.Create(
                    ClientReferentAccountId.New(),
                    clientId,
                    request.OrganizationId,
                    item.SocialNetworkId,
                    item.ProfileUrl,
                    item.Handle,
                    item.Label,
                    item.Notes,
                    _clock.UtcNow,
                    item.SortOrder);

                if (created.IsFailure)
                {
                    return Result.Failure<IReadOnlyList<ClientReferentAccountResponse>>(created.Error);
                }

                account = created.Value;
                ApplyActiveState(account, item.IsActive);
                await _accounts.AddAsync(account, cancellationToken);
            }
            else
            {
                var updated = account.Update(
                    item.SocialNetworkId,
                    item.ProfileUrl,
                    item.Handle,
                    item.Label,
                    item.Notes,
                    _clock.UtcNow);

                if (updated.IsFailure)
                {
                    return Result.Failure<IReadOnlyList<ClientReferentAccountResponse>>(updated.Error);
                }

                ApplyActiveState(account, item.IsActive);
                account.SetSortOrder(item.SortOrder, _clock.UtcNow);
                _accounts.Update(account);
            }

            claimed.Add(account.Id.Value);
            resultAccounts.Add(account);
        }

        foreach (var omitted in existing.Where(x => !claimed.Contains(x.Id.Value)))
        {
            if (omitted.IsActive)
            {
                var deactivated = omitted.Deactivate(_clock.UtcNow);
                if (deactivated.IsFailure)
                {
                    return Result.Failure<IReadOnlyList<ClientReferentAccountResponse>>(deactivated.Error);
                }

                _accounts.Update(omitted);
            }

            resultAccounts.Add(omitted);
        }

        await _accounts.SaveChangesAsync(cancellationToken);

        var response = resultAccounts
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAtUtc)
            .Select(account => ClientReferentAccountResponse.FromDomain(
                account,
                networkById[account.SocialNetworkId]))
            .ToList();

        return Result.Success<IReadOnlyList<ClientReferentAccountResponse>>(response);
    }

    private void ApplyActiveState(ClientReferentAccount account, bool shouldBeActive)
    {
        if (shouldBeActive && !account.IsActive)
        {
            account.Activate(_clock.UtcNow);
        }
        else if (!shouldBeActive && account.IsActive)
        {
            account.Deactivate(_clock.UtcNow);
        }
    }

    private readonly record struct AccountKey(SocialNetworkId SocialNetworkId, string ProfileUrl);

    private sealed record IncomingAccount(
        Guid? Id,
        SocialNetworkId SocialNetworkId,
        string ProfileUrl,
        string? Handle,
        string? Label,
        string? Notes,
        bool IsActive,
        int SortOrder);
}
