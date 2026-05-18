using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientReferentAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientReferentAccounts.GetByClient;

public sealed class ListClientReferentAccountsByClientHandler
    : IRequestHandler<ListClientReferentAccountsByClientQuery, Result<PagedClientReferentAccountsResponse>>
{
    private readonly IClientReferentAccountRepository _accounts;
    private readonly ISocialClientRepository _clients;
    private readonly ISocialNetworkRepository _networks;

    public ListClientReferentAccountsByClientHandler(
        IClientReferentAccountRepository accounts,
        ISocialClientRepository clients,
        ISocialNetworkRepository networks)
    {
        _accounts = accounts;
        _clients = clients;
        _networks = networks;
    }

    public async Task<Result<PagedClientReferentAccountsResponse>> Handle(
        ListClientReferentAccountsByClientQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<PagedClientReferentAccountsResponse>(ClientReferentAccountErrors.NotFound);
        }

        var (items, totalCount) = await _accounts.ListByClientAsync(
            clientId,
            request.OrganizationId,
            request.IncludeInactive,
            request.Page,
            request.PageSize,
            cancellationToken);

        // Resolution 4: batch-load networks to avoid N+1
        var networkIds = items.Select(a => a.SocialNetworkId).Distinct().ToList();
        var networks = await _networks.GetByIdsAsync(networkIds, cancellationToken);
        var networkMap = networks.ToDictionary(n => n.Id);

        var responses = items
            .Select(a => networkMap.TryGetValue(a.SocialNetworkId, out var network)
                ? ClientReferentAccountResponse.FromDomain(a, network)
                : null)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        return Result.Success(new PagedClientReferentAccountsResponse(responses, totalCount));
    }
}
