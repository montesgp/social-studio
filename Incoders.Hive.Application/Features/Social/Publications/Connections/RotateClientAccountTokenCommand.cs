using FluentValidation;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientAccountConnections;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Publications.Connections;

/// <summary>
/// Rotates the access token on an existing <see cref="ClientAccountConnection"/>. Re-protects the
/// plaintext BEFORE it reaches the aggregate — mirrors <see cref="CreateClientAccountConnectionCommand"/>.
/// </summary>
public sealed record RotateClientAccountTokenCommand(
    Guid OrganizationId,
    Guid ConnectionId,
    string AccessToken,
    DateTime? TokenExpiresAtUtc)
    : IRequest<Result<ClientAccountConnectionResponse>>
{
    // Defense-in-depth: carries a plaintext AccessToken; never let the default record ToString() print it.
    public override string ToString() => "RotateClientAccountTokenCommand { [redacted] }";
}

public sealed class RotateClientAccountTokenCommandValidator
    : AbstractValidator<RotateClientAccountTokenCommand>
{
    public RotateClientAccountTokenCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ConnectionId).NotEmpty();
        RuleFor(x => x.AccessToken).NotEmpty();
    }
}

public sealed class RotateClientAccountTokenCommandHandler
    : IRequestHandler<RotateClientAccountTokenCommand, Result<ClientAccountConnectionResponse>>
{
    private readonly IClientAccountConnectionRepository _connections;
    private readonly ISocialNetworkRepository _networks;
    private readonly IIntegrationSecretProtector _protector;
    private readonly IClock _clock;

    public RotateClientAccountTokenCommandHandler(
        IClientAccountConnectionRepository connections,
        ISocialNetworkRepository networks, IIntegrationSecretProtector protector, IClock clock)
    {
        _connections = connections;
        _networks = networks;
        _protector = protector;
        _clock = clock;
    }

    public async Task<Result<ClientAccountConnectionResponse>> Handle(
        RotateClientAccountTokenCommand request, CancellationToken ct)
    {
        var connection = await _connections.GetByIdAsync(
            request.OrganizationId, new ClientAccountConnectionId(request.ConnectionId), ct);

        if (connection is null)
        {
            return Result.Failure<ClientAccountConnectionResponse>(
                ClientAccountConnectionErrors.Validation("Connection not found."));
        }

        var encryptedAccessToken = _protector.Protect(request.AccessToken);
        var rotated = connection.RotateToken(encryptedAccessToken, request.TokenExpiresAtUtc, _clock.UtcNow);
        if (rotated.IsFailure)
        {
            return Result.Failure<ClientAccountConnectionResponse>(rotated.Error);
        }

        await _connections.UpdateAsync(connection, ct);
        return Result.Success(ClientAccountConnectionResponse.FromDomain(
            connection, await _networks.GetByIdAsync(connection.SocialNetworkId, ct)));
    }
}
