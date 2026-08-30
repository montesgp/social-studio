using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;

namespace Incoders.Hive.Domain.Social.ClientAccountConnections;

/// <summary>
/// Aggregate root for the per-client publish credential: which account (<see cref="ExternalAccountId"/>)
/// on which network this client publishes to, and the encrypted access token authorizing it. Accepts
/// and stores only ciphertext produced upstream by <c>IIntegrationSecretProtector.Protect</c> — this
/// aggregate never sees plaintext.
/// </summary>
public sealed class ClientAccountConnection : Entity<ClientAccountConnectionId>
{
    private ClientAccountConnection(
        ClientAccountConnectionId id,
        Guid organizationId,
        ClientId clientId,
        SocialNetworkId socialNetworkId,
        OrganizationIntegrationId organizationIntegrationId,
        string externalAccountId,
        string encryptedAccessToken,
        DateTime? tokenExpiresAtUtc,
        ClientAccountConnectionStatus status,
        DateTime? lastTestedAtUtc,
        string? lastError,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        OrganizationId = organizationId;
        ClientId = clientId;
        SocialNetworkId = socialNetworkId;
        OrganizationIntegrationId = organizationIntegrationId;
        ExternalAccountId = externalAccountId;
        EncryptedAccessToken = encryptedAccessToken;
        TokenExpiresAtUtc = tokenExpiresAtUtc;
        Status = status;
        LastTestedAtUtc = lastTestedAtUtc;
        LastError = lastError;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    // Parameterless constructor for EF Core
    private ClientAccountConnection() : base(default) { }

    public Guid OrganizationId { get; private set; }

    public ClientId ClientId { get; private set; }

    public SocialNetworkId SocialNetworkId { get; private set; }

    /// <summary>The org-level app this account authorizes against.</summary>
    public OrganizationIntegrationId OrganizationIntegrationId { get; private set; }

    /// <summary>Graph API IG business account / user id.</summary>
    public string ExternalAccountId { get; private set; } = string.Empty;

    /// <summary>Ciphertext produced by <c>IIntegrationSecretProtector.Protect</c>. Never plaintext.</summary>
    public string EncryptedAccessToken { get; private set; } = string.Empty;

    public DateTime? TokenExpiresAtUtc { get; private set; }

    public ClientAccountConnectionStatus Status { get; private set; }

    public DateTime? LastTestedAtUtc { get; private set; }

    public string? LastError { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<ClientAccountConnection> Create(
        ClientAccountConnectionId id,
        Guid organizationId,
        ClientId clientId,
        SocialNetworkId socialNetworkId,
        OrganizationIntegrationId organizationIntegrationId,
        string externalAccountId,
        string encryptedAccessToken,
        DateTime? tokenExpiresAtUtc,
        DateTime nowUtc)
    {
        var validation = Validate(organizationIntegrationId, externalAccountId, encryptedAccessToken);
        if (validation.IsFailure)
        {
            return Result.Failure<ClientAccountConnection>(validation.Error);
        }

        return Result.Success(new ClientAccountConnection(
            id, organizationId, clientId, socialNetworkId, organizationIntegrationId,
            externalAccountId.Trim(), encryptedAccessToken, tokenExpiresAtUtc,
            ClientAccountConnectionStatus.Connected, lastTestedAtUtc: null, lastError: null,
            nowUtc, nowUtc));
    }

    /// <summary>Re-hydration factory used by EF mapping. Bypasses validation — data is trusted.</summary>
    public static ClientAccountConnection Restore(
        ClientAccountConnectionId id,
        Guid organizationId,
        ClientId clientId,
        SocialNetworkId socialNetworkId,
        OrganizationIntegrationId organizationIntegrationId,
        string externalAccountId,
        string encryptedAccessToken,
        DateTime? tokenExpiresAtUtc,
        ClientAccountConnectionStatus status,
        DateTime? lastTestedAtUtc,
        string? lastError,
        DateTime createdAtUtc,
        DateTime updatedAtUtc) =>
        new(id, organizationId, clientId, socialNetworkId, organizationIntegrationId,
            externalAccountId, encryptedAccessToken, tokenExpiresAtUtc, status,
            lastTestedAtUtc, lastError, createdAtUtc, updatedAtUtc);

    /// <summary>Replaces the ciphertext and expiry. Accepts an already-encrypted value — never
    /// plaintext. Rejected once the connection has been <see cref="Disable"/>d.</summary>
    public Result RotateToken(string encryptedAccessToken, DateTime? tokenExpiresAtUtc, DateTime nowUtc)
    {
        if (Status == ClientAccountConnectionStatus.Disabled)
        {
            return Result.Failure(ClientAccountConnectionErrors.Disabled);
        }

        if (string.IsNullOrWhiteSpace(encryptedAccessToken))
        {
            return Result.Failure(ClientAccountConnectionErrors.Validation("Encrypted access token is required."));
        }

        EncryptedAccessToken = encryptedAccessToken;
        TokenExpiresAtUtc = tokenExpiresAtUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Records the outcome of a "Test connection" action against the provider.</summary>
    public void MarkTestResult(bool isSuccess, string? error, DateTime nowUtc)
    {
        Status = isSuccess ? ClientAccountConnectionStatus.Connected : ClientAccountConnectionStatus.Failed;
        LastTestedAtUtc = nowUtc;
        LastError = isSuccess ? null : (string.IsNullOrWhiteSpace(error) ? null : error.Trim());
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Terminal-ish guard: once disabled, further token rotation is rejected.</summary>
    public Result Disable(DateTime nowUtc)
    {
        Status = ClientAccountConnectionStatus.Disabled;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    private static Result Validate(
        OrganizationIntegrationId organizationIntegrationId, string externalAccountId, string encryptedAccessToken)
    {
        if (string.IsNullOrWhiteSpace(externalAccountId))
        {
            return Result.Failure(ClientAccountConnectionErrors.Validation("External account id is required."));
        }

        if (string.IsNullOrWhiteSpace(encryptedAccessToken))
        {
            return Result.Failure(ClientAccountConnectionErrors.Validation("Encrypted access token is required."));
        }

        if (organizationIntegrationId.Value == Guid.Empty)
        {
            return Result.Failure(ClientAccountConnectionErrors.Validation("Organization integration id is required."));
        }

        return Result.Success();
    }
}
