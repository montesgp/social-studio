using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientReferentAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Create;

public sealed class CreateClientReferentAccountHandler
    : IRequestHandler<CreateClientReferentAccountCommand, Result<ClientReferentAccountResponse>>
{
    private readonly IClientReferentAccountRepository _accounts;
    private readonly ISocialClientRepository _clients;
    private readonly ISocialNetworkRepository _networks;
    private readonly IClock _clock;

    public CreateClientReferentAccountHandler(
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

    public async Task<Result<ClientReferentAccountResponse>> Handle(
        CreateClientReferentAccountCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<ClientReferentAccountResponse>(ClientReferentAccountErrors.NotFound);
        }

        if (!client.IsActive)
        {
            return Result.Failure<ClientReferentAccountResponse>(
                ClientReferentAccountErrors.Validation("The client is inactive."));
        }

        var networkId = new SocialNetworkId(request.SocialNetworkId);
        var network = await _networks.GetByIdAsync(networkId, cancellationToken);
        if (network is null)
        {
            return Result.Failure<ClientReferentAccountResponse>(ClientReferentAccountErrors.NotFound);
        }

        if (!network.IsActive)
        {
            return Result.Failure<ClientReferentAccountResponse>(ClientReferentAccountErrors.SocialNetworkInactive);
        }

        var profileUrl = request.ProfileUrl?.Trim() ?? string.Empty;
        var exists = await _accounts.ExistsByProfileUrlAsync(clientId, networkId, profileUrl, null, cancellationToken);
        if (exists)
        {
            return Result.Failure<ClientReferentAccountResponse>(
                ClientReferentAccountErrors.ProfileUrlAlreadyExists(profileUrl));
        }

        var created = ClientReferentAccount.Create(
            ClientReferentAccountId.New(),
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
            return Result.Failure<ClientReferentAccountResponse>(created.Error);
        }

        await _accounts.AddAsync(created.Value, cancellationToken);
        await _accounts.SaveChangesAsync(cancellationToken);

        return Result.Success(ClientReferentAccountResponse.FromDomain(created.Value, network));
    }
}
