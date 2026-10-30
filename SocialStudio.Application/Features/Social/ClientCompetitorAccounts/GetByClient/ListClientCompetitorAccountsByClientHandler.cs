using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientCompetitorAccounts;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Networks;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientCompetitorAccounts.GetByClient;

public sealed class ListClientCompetitorAccountsByClientHandler
    : IRequestHandler<ListClientCompetitorAccountsByClientQuery, Result<PagedClientCompetitorAccountsResponse>>
{
    private readonly IClientCompetitorAccountRepository _accounts;
    private readonly ISocialClientRepository _clients;
    private readonly ISocialNetworkRepository _networks;

    public ListClientCompetitorAccountsByClientHandler(
        IClientCompetitorAccountRepository accounts,
        ISocialClientRepository clients,
        ISocialNetworkRepository networks)
    {
        _accounts = accounts;
        _clients = clients;
        _networks = networks;
    }

    public async Task<Result<PagedClientCompetitorAccountsResponse>> Handle(
        ListClientCompetitorAccountsByClientQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<PagedClientCompetitorAccountsResponse>(ClientCompetitorAccountErrors.NotFound);
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
                ? ClientCompetitorAccountResponse.FromDomain(a, network)
                : null)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        return Result.Success(new PagedClientCompetitorAccountsResponse(responses, totalCount));
    }
}
