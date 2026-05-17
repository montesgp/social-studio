using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Integrations;

public sealed class OrganizationIntegrationCredential : Entity<OrganizationIntegrationCredentialId>
{
    public const int KeyMaxLength = 64;

    private OrganizationIntegrationCredential(
        OrganizationIntegrationCredentialId id,
        OrganizationIntegrationId organizationIntegrationId,
        string key,
        string encryptedValue,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        OrganizationIntegrationId = organizationIntegrationId;
        Key = key;
        EncryptedValue = encryptedValue;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public OrganizationIntegrationId OrganizationIntegrationId { get; private set; }

    public string Key { get; private set; }

    public string EncryptedValue { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<OrganizationIntegrationCredential> Create(
        OrganizationIntegrationCredentialId id,
        OrganizationIntegrationId organizationIntegrationId,
        string key,
        string encryptedValue,
        DateTime nowUtc)
    {
        var normalizedKey = key?.Trim().ToLowerInvariant() ?? string.Empty;

        if (organizationIntegrationId.Value == Guid.Empty)
        {
            return Result.Failure<OrganizationIntegrationCredential>(IntegrationErrors.IntegrationNotFound);
        }

        if (string.IsNullOrWhiteSpace(normalizedKey) || normalizedKey.Length > KeyMaxLength)
        {
            return Result.Failure<OrganizationIntegrationCredential>(IntegrationErrors.CredentialKeyRequired);
        }

        if (string.IsNullOrWhiteSpace(encryptedValue))
        {
            return Result.Failure<OrganizationIntegrationCredential>(IntegrationErrors.CredentialValueRequired);
        }

        return Result.Success(new OrganizationIntegrationCredential(
            id,
            organizationIntegrationId,
            normalizedKey,
            encryptedValue,
            nowUtc,
            nowUtc));
    }

    public static OrganizationIntegrationCredential Restore(
        OrganizationIntegrationCredentialId id,
        OrganizationIntegrationId organizationIntegrationId,
        string key,
        string encryptedValue,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new OrganizationIntegrationCredential(
            id,
            organizationIntegrationId,
            key,
            encryptedValue,
            createdAtUtc,
            updatedAtUtc);
    }

    public Result Rotate(string encryptedValue, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(encryptedValue))
        {
            return Result.Failure(IntegrationErrors.CredentialValueRequired);
        }

        EncryptedValue = encryptedValue;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}