using FluentValidation;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ClientAccountConnections;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Publications.Connections;

/// <summary>
/// Creates the client's per-network publish credential. The plaintext <see cref="AccessToken"/> is
/// protected via <see cref="IIntegrationSecretProtector.Protect"/> BEFORE the aggregate is
/// constructed — the aggregate and its persisted row only ever see ciphertext.
/// </summary>
public sealed record CreateClientAccountConnectionCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid SocialNetworkId,
    Guid OrganizationIntegrationId,
    string ExternalAccountId,
    string AccessToken,
    DateTime? TokenExpiresAtUtc)
    : IRequest<Result<ClientAccountConnectionResponse>>
{
    // Defense-in-depth: carries a plaintext AccessToken; never let the default record ToString() print it.
    public override string ToString() => "CreateClientAccountConnectionCommand { [redacted] }";
}

public sealed class CreateClientAccountConnectionCommandValidator
    : AbstractValidator<CreateClientAccountConnectionCommand>
{
    public CreateClientAccountConnectionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.SocialNetworkId).NotEmpty();
        RuleFor(x => x.OrganizationIntegrationId).NotEmpty();
        RuleFor(x => x.ExternalAccountId).NotEmpty();
        RuleFor(x => x.AccessToken).NotEmpty();
    }
}

public sealed class CreateClientAccountConnectionCommandHandler
    : IRequestHandler<CreateClientAccountConnectionCommand, Result<ClientAccountConnectionResponse>>
{
    private readonly IClientAccountConnectionRepository _connections;
    private readonly IIntegrationSecretProtector _protector;
    private readonly IClock _clock;

    public CreateClientAccountConnectionCommandHandler(
        IClientAccountConnectionRepository connections, IIntegrationSecretProtector protector, IClock clock)
    {
        _connections = connections;
        _protector = protector;
        _clock = clock;
    }

    public async Task<Result<ClientAccountConnectionResponse>> Handle(
        CreateClientAccountConnectionCommand request, CancellationToken ct)
    {
        // Protect the plaintext BEFORE constructing the aggregate — it never sees plaintext.
        var encryptedAccessToken = _protector.Protect(request.AccessToken);

        var created = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(),
            request.OrganizationId,
            new ClientId(request.ClientId),
            new SocialNetworkId(request.SocialNetworkId),
            new OrganizationIntegrationId(request.OrganizationIntegrationId),
            request.ExternalAccountId,
            encryptedAccessToken,
            request.TokenExpiresAtUtc,
            _clock.UtcNow);

        if (created.IsFailure)
        {
            return Result.Failure<ClientAccountConnectionResponse>(created.Error);
        }

        await _connections.AddAsync(created.Value, ct);
        return Result.Success(ClientAccountConnectionResponse.FromDomain(created.Value));
    }
}
