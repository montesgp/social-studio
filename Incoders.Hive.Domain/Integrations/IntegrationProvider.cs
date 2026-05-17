using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Integrations;

public sealed class IntegrationProvider : Entity<IntegrationProviderId>
{
    public const int KeyMaxLength = 64;
    public const int DisplayNameMaxLength = 120;
    public const int IconKeyMaxLength = 64;

    private IntegrationProvider(
        IntegrationProviderId id,
        string key,
        string displayName,
        IntegrationProviderCategory category,
        string? description,
        string iconKey,
        bool supportsBringYourOwnKey,
        bool supportsManagedByIncoders,
        bool supportsSystemDefault,
        bool systemDefaultReadOnly,
        IReadOnlyList<IntegrationCredentialField> credentialFields,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        Key = key;
        DisplayName = displayName;
        Category = category;
        Description = description;
        IconKey = iconKey;
        SupportsBringYourOwnKey = supportsBringYourOwnKey;
        SupportsManagedByIncoders = supportsManagedByIncoders;
        SupportsSystemDefault = supportsSystemDefault;
        SystemDefaultReadOnly = systemDefaultReadOnly;
        CredentialFields = credentialFields;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public string Key { get; private set; }

    public string DisplayName { get; private set; }

    public IntegrationProviderCategory Category { get; private set; }

    public string? Description { get; private set; }

    public string IconKey { get; private set; }

    public bool SupportsBringYourOwnKey { get; private set; }

    public bool SupportsManagedByIncoders { get; private set; }

    public bool SupportsSystemDefault { get; private set; }

    public bool SystemDefaultReadOnly { get; private set; }

    public IReadOnlyList<IntegrationCredentialField> CredentialFields { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<IntegrationProvider> Create(
        IntegrationProviderId id,
        string key,
        string displayName,
        IntegrationProviderCategory category,
        string? description,
        string iconKey,
        bool supportsBringYourOwnKey,
        bool supportsManagedByIncoders,
        bool supportsSystemDefault,
        bool systemDefaultReadOnly,
        IReadOnlyList<IntegrationCredentialField> credentialFields,
        DateTime nowUtc)
    {
        var normalizedKey = key?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedName = displayName?.Trim() ?? string.Empty;
        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        var normalizedIconKey = iconKey?.Trim().ToLowerInvariant() ?? string.Empty;
        var fields = credentialFields ?? [];

        if (string.IsNullOrWhiteSpace(normalizedKey) || normalizedKey.Length > KeyMaxLength)
        {
            return Result.Failure<IntegrationProvider>(IntegrationErrors.ProviderKeyRequired);
        }

        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > DisplayNameMaxLength)
        {
            return Result.Failure<IntegrationProvider>(IntegrationErrors.ProviderNameRequired);
        }

        if (string.IsNullOrWhiteSpace(normalizedIconKey) || normalizedIconKey.Length > IconKeyMaxLength)
        {
            return Result.Failure<IntegrationProvider>(IntegrationErrors.ProviderIconKeyRequired);
        }

        if (!supportsBringYourOwnKey && !supportsManagedByIncoders && !supportsSystemDefault)
        {
            return Result.Failure<IntegrationProvider>(IntegrationErrors.ProviderModeRequired);
        }

        if (systemDefaultReadOnly && !supportsSystemDefault)
        {
            return Result.Failure<IntegrationProvider>(IntegrationErrors.ConnectionModeNotSupported);
        }

        var duplicateField = fields
            .Select(x => x.Key?.Trim().ToLowerInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x)
            .Any(group => group.Count() > 1);

        if (duplicateField)
        {
            return Result.Failure<IntegrationProvider>(IntegrationErrors.ProviderCredentialFieldDuplicate);
        }

        return Result.Success(new IntegrationProvider(
            id,
            normalizedKey,
            normalizedName,
            category,
            normalizedDescription,
            normalizedIconKey,
            supportsBringYourOwnKey,
            supportsManagedByIncoders,
            supportsSystemDefault,
            systemDefaultReadOnly,
            fields,
            true,
            nowUtc,
            nowUtc));
    }

    public static IntegrationProvider Restore(
        IntegrationProviderId id,
        string key,
        string displayName,
        IntegrationProviderCategory category,
        string? description,
        string iconKey,
        bool supportsBringYourOwnKey,
        bool supportsManagedByIncoders,
        bool supportsSystemDefault,
        bool systemDefaultReadOnly,
        IReadOnlyList<IntegrationCredentialField> credentialFields,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new IntegrationProvider(
            id,
            key,
            displayName,
            category,
            description,
            iconKey,
            supportsBringYourOwnKey,
            supportsManagedByIncoders,
            supportsSystemDefault,
            systemDefaultReadOnly,
            credentialFields,
            isActive,
            createdAtUtc,
            updatedAtUtc);
    }

    public bool SupportsMode(IntegrationConnectionMode mode)
    {
        return mode switch
        {
            IntegrationConnectionMode.BringYourOwnKey => SupportsBringYourOwnKey,
            IntegrationConnectionMode.ManagedByIncoders => SupportsManagedByIncoders,
            IntegrationConnectionMode.SystemDefault => SupportsSystemDefault,
            _ => false,
        };
    }
}
