using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientOwnAccounts;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Networks;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientOwnAccounts.GetByClient;

public sealed class ListClientOwnAccountsByClientHandler
    : IRequestHandler<ListClientOwnAccountsByClientQuery, Result<PagedClientOwnAccountsResponse>>
{
    private readonly IClientOwnAccountRepository _accounts;
    private readonly ISocialClientRepository _clients;
    private readonly ISocialNetworkRepository _networks;

    public ListClientOwnAccountsByClientHandler(
        IClientOwnAccountRepository accounts,
        ISocialClientRepository clients,
        ISocialNetworkRepository networks)
    {
        _accounts = accounts;
        _clients = clients;
        _networks = networks;
    }

    public async Task<Result<PagedClientOwnAccountsResponse>> Handle(
        ListClientOwnAccountsByClientQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<PagedClientOwnAccountsResponse>(ClientOwnAccountErrors.NotFound);
        }

        var (items, totalCount) = await _accounts.ListByClientAsync(
            clientId,
            request.OrganizationId,
            request.IncludeInactive,
            request.Page,
            request.PageSize,
            cancellationToken);

        var networkIds = items.Select(a => a.SocialNetworkId).Distinct().ToList();
        var networks = await _networks.GetByIdsAsync(networkIds, cancellationToken);
        var networkMap = networks.ToDictionary(n => n.Id);

        var responses = items
            .Select(a => networkMap.TryGetValue(a.SocialNetworkId, out var network)
                ? ClientOwnAccountResponse.FromDomain(a, network)
                : null)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        return Result.Success(new PagedClientOwnAccountsResponse(responses, totalCount));
    }
}
