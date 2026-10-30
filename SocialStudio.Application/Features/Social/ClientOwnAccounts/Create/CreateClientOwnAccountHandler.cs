using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientOwnAccounts;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Networks;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientOwnAccounts.Create;

public sealed class CreateClientOwnAccountHandler
    : IRequestHandler<CreateClientOwnAccountCommand, Result<ClientOwnAccountResponse>>
{
    private readonly IClientOwnAccountRepository _accounts;
    private readonly ISocialClientRepository _clients;
    private readonly ISocialNetworkRepository _networks;
    private readonly IClock _clock;

    public CreateClientOwnAccountHandler(
        IClientOwnAccountRepository accounts,
        ISocialClientRepository clients,
        ISocialNetworkRepository networks,
        IClock clock)
    {
        _accounts = accounts;
        _clients = clients;
        _networks = networks;
        _clock = clock;
    }

    public async Task<Result<ClientOwnAccountResponse>> Handle(
        CreateClientOwnAccountCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<ClientOwnAccountResponse>(ClientOwnAccountErrors.NotFound);
        }

        if (!client.IsActive)
        {
            return Result.Failure<ClientOwnAccountResponse>(
                ClientOwnAccountErrors.Validation("The client is inactive."));
        }

        var networkId = new SocialNetworkId(request.SocialNetworkId);
        var network = await _networks.GetByIdAsync(networkId, cancellationToken);
        if (network is null)
        {
            return Result.Failure<ClientOwnAccountResponse>(ClientOwnAccountErrors.NotFound);
        }

        if (!network.IsActive)
        {
            return Result.Failure<ClientOwnAccountResponse>(ClientOwnAccountErrors.SocialNetworkInactive);
        }

        var profileUrl = request.ProfileUrl?.Trim() ?? string.Empty;
        var exists = await _accounts.ExistsByProfileUrlAsync(clientId, networkId, profileUrl, null, cancellationToken);
        if (exists)
        {
            return Result.Failure<ClientOwnAccountResponse>(
                ClientOwnAccountErrors.ProfileUrlAlreadyExists(profileUrl));
        }

        var created = ClientOwnAccount.Create(
            ClientOwnAccountId.New(),
            clientId,
            request.OrganizationId,
            networkId,
            profileUrl,
            request.Handle,
            request.Label,
            request.Notes,
            _clock.UtcNow);

        if (created.IsFailure)
        {
            return Result.Failure<ClientOwnAccountResponse>(created.Error);
        }

        await _accounts.AddAsync(created.Value, cancellationToken);
        await _accounts.SaveChangesAsync(cancellationToken);

        return Result.Success(ClientOwnAccountResponse.FromDomain(created.Value, network));
    }
}
