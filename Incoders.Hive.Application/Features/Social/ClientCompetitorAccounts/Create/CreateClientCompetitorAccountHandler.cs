using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientCompetitorAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientCompetitorAccounts.Create;

public sealed class CreateClientCompetitorAccountHandler
    : IRequestHandler<CreateClientCompetitorAccountCommand, Result<ClientCompetitorAccountResponse>>
{
    private readonly IClientCompetitorAccountRepository _accounts;
    private readonly ISocialClientRepository _clients;
    private readonly ISocialNetworkRepository _networks;
    private readonly IClock _clock;

    public CreateClientCompetitorAccountHandler(
        IClientCompetitorAccountRepository accounts,
        ISocialClientRepository clients,
        ISocialNetworkRepository networks,
        IClock clock)
    {
        _accounts = accounts;
        _clients = clients;
        _networks = networks;
        _clock = clock;
    }

    public async Task<Result<ClientCompetitorAccountResponse>> Handle(
        CreateClientCompetitorAccountCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<ClientCompetitorAccountResponse>(ClientCompetitorAccountErrors.NotFound);
        }

        if (!client.IsActive)
        {
            return Result.Failure<ClientCompetitorAccountResponse>(
                ClientCompetitorAccountErrors.Validation("The client is inactive."));
        }

        var networkId = new SocialNetworkId(request.SocialNetworkId);
        var network = await _networks.GetByIdAsync(networkId, cancellationToken);
        if (network is null)
        {
            return Result.Failure<ClientCompetitorAccountResponse>(ClientCompetitorAccountErrors.NotFound);
        }

        if (!network.IsActive)
        {
            return Result.Failure<ClientCompetitorAccountResponse>(ClientCompetitorAccountErrors.SocialNetworkInactive);
        }

        var profileUrl = request.ProfileUrl?.Trim() ?? string.Empty;
        var exists = await _accounts.ExistsByProfileUrlAsync(clientId, networkId, profileUrl, null, cancellationToken);
        if (exists)
        {
            return Result.Failure<ClientCompetitorAccountResponse>(
                ClientCompetitorAccountErrors.ProfileUrlAlreadyExists(profileUrl));
        }

        var created = ClientCompetitorAccount.Create(
            ClientCompetitorAccountId.New(),
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
            return Result.Failure<ClientCompetitorAccountResponse>(created.Error);
        }

        await _accounts.AddAsync(created.Value, cancellationToken);
        await _accounts.SaveChangesAsync(cancellationToken);

        return Result.Success(ClientCompetitorAccountResponse.FromDomain(created.Value, network));
    }
}
