using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Integrations;

public sealed class IntegrationProvider : Entity<IntegrationProviderId>
{
    public const int KeyMaxLength = 64;
    public const int DisplayNameMaxLength = 120;
    public const int IconKeyMaxLength = 64;

    private IntegrationProvider(
        IntegrationProviderId id,
        string key,
        string displayName,
        IReadOnlyList<IntegrationProviderCategory> categories,
        string? description,
        string iconKey,
        bool supportsBringYourOwnKey,
        bool supportsManagedByIncoders,
        bool supportsSystemDefault,
        bool systemDefaultReadOnly,
        IReadOnlyList<IntegrationCredentialField> credentialFields,
        IReadOnlyDictionary<IntegrationProviderCategory, IReadOnlyList<string>> modelsByCategory,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        Key = key;
        DisplayName = displayName;
        Categories = categories;
        Description = description;
        IconKey = iconKey;
        SupportsBringYourOwnKey = supportsBringYourOwnKey;
        SupportsManagedByIncoders = supportsManagedByIncoders;
        SupportsSystemDefault = supportsSystemDefault;
        SystemDefaultReadOnly = systemDefaultReadOnly;
        CredentialFields = credentialFields;
        ModelsByCategory = modelsByCategory;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public string Key { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>
    /// All catalog categories this provider serves. A single connector (one credential) can be
    /// presented under multiple Integrations sections — e.g. OpenAI under both AI model providers
    /// and Media generation providers. The first entry is the primary category.
    /// </summary>
    public IReadOnlyList<IntegrationProviderCategory> Categories { get; private set; }

    /// <summary>Primary category — back-compat for consumers that assume a single category.</summary>
    public IntegrationProviderCategory Category => Categories[0];

    public string? Description { get; private set; }

    public string IconKey { get; private set; }

    public bool SupportsBringYourOwnKey { get; private set; }

    public bool SupportsManagedByIncoders { get; private set; }

    public bool SupportsSystemDefault { get; private set; }

    public bool SystemDefaultReadOnly { get; private set; }

    public IReadOnlyList<IntegrationCredentialField> CredentialFields { get; private set; }

    /// <summary>Catalog model menus keyed by the category they belong to.</summary>
    public IReadOnlyDictionary<IntegrationProviderCategory, IReadOnlyList<string>> ModelsByCategory { get; private set; }

    /// <summary>Models for the primary category — back-compat for single-category consumers.</summary>
    public IReadOnlyList<string> AvailableModels => ModelsFor(Category);

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public bool ServesCategory(IntegrationProviderCategory category) => Categories.Contains(category);

    public IReadOnlyList<string> ModelsFor(IntegrationProviderCategory category) =>
        ModelsByCategory.TryGetValue(category, out var models) ? models : [];

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
        IReadOnlyList<string> availableModels,
        DateTime nowUtc)
    {
        return Create(
            id,
            key,
            displayName,
            [category],
            description,
            iconKey,
            supportsBringYourOwnKey,
            supportsManagedByIncoders,
            supportsSystemDefault,
            systemDefaultReadOnly,
            credentialFields,
            new Dictionary<IntegrationProviderCategory, IReadOnlyList<string>>
            {
                [category] = availableModels ?? [],
            },
            nowUtc);
    }

    public static Result<IntegrationProvider> Create(
        IntegrationProviderId id,
        string key,
        string displayName,
        IReadOnlyList<IntegrationProviderCategory> categories,
        string? description,
        string iconKey,
        bool supportsBringYourOwnKey,
        bool supportsManagedByIncoders,
        bool supportsSystemDefault,
        bool systemDefaultReadOnly,
        IReadOnlyList<IntegrationCredentialField> credentialFields,
        IReadOnlyDictionary<IntegrationProviderCategory, IReadOnlyList<string>> modelsByCategory,
        DateTime nowUtc)
    {
        var normalizedKey = key?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedName = displayName?.Trim() ?? string.Empty;
        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        var normalizedIconKey = iconKey?.Trim().ToLowerInvariant() ?? string.Empty;
        var fields = credentialFields ?? [];
        var normalizedCategories = (categories ?? []).Distinct().ToList();

        if (string.IsNullOrWhiteSpace(normalizedKey) || normalizedKey.Length > KeyMaxLength)
        {
            return Result.Failure<IntegrationProvider>(IntegrationErrors.ProviderKeyRequired);
        }

        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > DisplayNameMaxLength)
        {
            return Result.Failure<IntegrationProvider>(IntegrationErrors.ProviderNameRequired);
        }

        if (normalizedCategories.Count == 0)
        {
            return Result.Failure<IntegrationProvider>(IntegrationErrors.ProviderCategoryRequired);
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

        var models = NormalizeModels(normalizedCategories, modelsByCategory);

        return Result.Success(new IntegrationProvider(
            id,
            normalizedKey,
            normalizedName,
            normalizedCategories,
            normalizedDescription,
            normalizedIconKey,
            supportsBringYourOwnKey,
            supportsManagedByIncoders,
            supportsSystemDefault,
            systemDefaultReadOnly,
            fields,
            models,
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
        IReadOnlyList<string> availableModels,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return Restore(
            id,
            key,
            displayName,
            [category],
            description,
            iconKey,
            supportsBringYourOwnKey,
            supportsManagedByIncoders,
            supportsSystemDefault,
            systemDefaultReadOnly,
            credentialFields,
            new Dictionary<IntegrationProviderCategory, IReadOnlyList<string>>
            {
                [category] = availableModels ?? [],
            },
            isActive,
            createdAtUtc,
            updatedAtUtc);
    }

    public static IntegrationProvider Restore(
        IntegrationProviderId id,
        string key,
        string displayName,
        IReadOnlyList<IntegrationProviderCategory> categories,
        string? description,
        string iconKey,
        bool supportsBringYourOwnKey,
        bool supportsManagedByIncoders,
        bool supportsSystemDefault,
        bool systemDefaultReadOnly,
        IReadOnlyList<IntegrationCredentialField> credentialFields,
        IReadOnlyDictionary<IntegrationProviderCategory, IReadOnlyList<string>> modelsByCategory,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        var normalizedCategories = (categories ?? []).Distinct().ToList();

        return new IntegrationProvider(
            id,
            key,
            displayName,
            normalizedCategories,
            description,
            iconKey,
            supportsBringYourOwnKey,
            supportsManagedByIncoders,
            supportsSystemDefault,
            systemDefaultReadOnly,
            credentialFields,
            NormalizeModels(normalizedCategories, modelsByCategory),
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

    private static IReadOnlyDictionary<IntegrationProviderCategory, IReadOnlyList<string>> NormalizeModels(
        IReadOnlyList<IntegrationProviderCategory> categories,
        IReadOnlyDictionary<IntegrationProviderCategory, IReadOnlyList<string>>? modelsByCategory)
    {
        var source = modelsByCategory ?? new Dictionary<IntegrationProviderCategory, IReadOnlyList<string>>();
        var normalized = new Dictionary<IntegrationProviderCategory, IReadOnlyList<string>>();

        foreach (var category in categories)
        {
            normalized[category] = source.TryGetValue(category, out var models) ? models : [];
        }

        return normalized;
    }
}
