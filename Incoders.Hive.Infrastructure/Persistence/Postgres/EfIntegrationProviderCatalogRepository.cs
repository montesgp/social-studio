using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfIntegrationProviderCatalogRepository : IIntegrationProviderCatalogRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppIdentityDbContext _dbContext;

    public EfIntegrationProviderCatalogRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<IntegrationProvider>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.IntegrationProviders
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<IntegrationProvider?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalizedKey = key.Trim().ToLowerInvariant();

        var row = await _dbContext.IntegrationProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == normalizedKey && x.IsActive, cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    public async Task<IntegrationProvider?> GetByIdAsync(IntegrationProviderId id, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.IntegrationProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value && x.IsActive, cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    public async Task UpsertAsync(IntegrationProvider provider, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.IntegrationProviders
            .FirstOrDefaultAsync(x => x.Key == provider.Key, cancellationToken);

        if (existing is null)
        {
            _dbContext.IntegrationProviders.Add(ToData(provider));
            return;
        }

        existing.DisplayName = provider.DisplayName;
        existing.CategoriesJson = SerializeCategories(provider.Categories);
        existing.Description = provider.Description;
        existing.IconKey = provider.IconKey;
        existing.SupportsBringYourOwnKey = provider.SupportsBringYourOwnKey;
        existing.SupportsManagedByIncoders = provider.SupportsManagedByIncoders;
        existing.SupportsSystemDefault = provider.SupportsSystemDefault;
        existing.SystemDefaultReadOnly = provider.SystemDefaultReadOnly;
        existing.CredentialFieldsJson = SerializeCredentialFields(provider.CredentialFields);
        existing.ModelsByCategoryJson = SerializeModelsByCategory(provider.ModelsByCategory);
        existing.IsActive = provider.IsActive;
        existing.UpdatedAtUtc = provider.UpdatedAtUtc;
    }

    private static IntegrationProvider ToDomain(IntegrationProviderData row)
    {
        var fields = DeserializeCredentialFields(row.CredentialFieldsJson);
        var categories = DeserializeCategories(row.CategoriesJson);
        var modelsByCategory = DeserializeModelsByCategory(row.ModelsByCategoryJson);

        return IntegrationProvider.Restore(
            new IntegrationProviderId(row.Id),
            row.Key,
            row.DisplayName,
            categories,
            row.Description,
            row.IconKey,
            row.SupportsBringYourOwnKey,
            row.SupportsManagedByIncoders,
            row.SupportsSystemDefault,
            row.SystemDefaultReadOnly,
            fields,
            modelsByCategory,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);
    }

    private static IntegrationProviderData ToData(IntegrationProvider provider) =>
        new()
        {
            Id = provider.Id.Value,
            Key = provider.Key,
            DisplayName = provider.DisplayName,
            CategoriesJson = SerializeCategories(provider.Categories),
            Description = provider.Description,
            IconKey = provider.IconKey,
            SupportsBringYourOwnKey = provider.SupportsBringYourOwnKey,
            SupportsManagedByIncoders = provider.SupportsManagedByIncoders,
            SupportsSystemDefault = provider.SupportsSystemDefault,
            SystemDefaultReadOnly = provider.SystemDefaultReadOnly,
            CredentialFieldsJson = SerializeCredentialFields(provider.CredentialFields),
            ModelsByCategoryJson = SerializeModelsByCategory(provider.ModelsByCategory),
            IsActive = provider.IsActive,
            CreatedAtUtc = provider.CreatedAtUtc,
            UpdatedAtUtc = provider.UpdatedAtUtc,
        };

    private static string SerializeCredentialFields(IReadOnlyList<IntegrationCredentialField> fields)
    {
        return JsonSerializer.Serialize(fields, JsonOptions);
    }

    private static IReadOnlyList<IntegrationCredentialField> DeserializeCredentialFields(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<IntegrationCredentialField>();
        }

        return JsonSerializer.Deserialize<List<IntegrationCredentialField>>(json, JsonOptions)
               ?? [];
    }

    private static string SerializeCategories(IReadOnlyList<IntegrationProviderCategory> categories)
    {
        return JsonSerializer.Serialize(categories.Select(x => x.ToString()).ToList(), JsonOptions);
    }

    private static IReadOnlyList<IntegrationProviderCategory> DeserializeCategories(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var names = JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];

        return names
            .Where(name => Enum.TryParse<IntegrationProviderCategory>(name, out _))
            .Select(Enum.Parse<IntegrationProviderCategory>)
            .ToList();
    }

    private static string SerializeModelsByCategory(
        IReadOnlyDictionary<IntegrationProviderCategory, IReadOnlyList<string>> modelsByCategory)
    {
        var byName = modelsByCategory.ToDictionary(x => x.Key.ToString(), x => x.Value);
        return JsonSerializer.Serialize(byName, JsonOptions);
    }

    private static IReadOnlyDictionary<IntegrationProviderCategory, IReadOnlyList<string>> DeserializeModelsByCategory(string json)
    {
        var result = new Dictionary<IntegrationProviderCategory, IReadOnlyList<string>>();

        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        var byName = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, JsonOptions)
                     ?? new Dictionary<string, List<string>>();

        foreach (var (name, models) in byName)
        {
            if (Enum.TryParse<IntegrationProviderCategory>(name, out var category))
            {
                result[category] = models;
            }
        }

        return result;
    }
}
