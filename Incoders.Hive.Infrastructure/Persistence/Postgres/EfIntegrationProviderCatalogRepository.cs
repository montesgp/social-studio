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
            .OrderBy(x => x.Category)
            .ThenBy(x => x.DisplayName)
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
        existing.Category = provider.Category.ToString();
        existing.Description = provider.Description;
        existing.IconKey = provider.IconKey;
        existing.SupportsBringYourOwnKey = provider.SupportsBringYourOwnKey;
        existing.SupportsManagedByIncoders = provider.SupportsManagedByIncoders;
        existing.SupportsSystemDefault = provider.SupportsSystemDefault;
        existing.SystemDefaultReadOnly = provider.SystemDefaultReadOnly;
        existing.CredentialFieldsJson = SerializeCredentialFields(provider.CredentialFields);
        existing.IsActive = provider.IsActive;
        existing.UpdatedAtUtc = provider.UpdatedAtUtc;
    }

    private static IntegrationProvider ToDomain(IntegrationProviderData row)
    {
        var fields = DeserializeCredentialFields(row.CredentialFieldsJson);

        return IntegrationProvider.Restore(
            new IntegrationProviderId(row.Id),
            row.Key,
            row.DisplayName,
            Enum.Parse<IntegrationProviderCategory>(row.Category),
            row.Description,
            row.IconKey,
            row.SupportsBringYourOwnKey,
            row.SupportsManagedByIncoders,
            row.SupportsSystemDefault,
            row.SystemDefaultReadOnly,
            fields,
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
            Category = provider.Category.ToString(),
            Description = provider.Description,
            IconKey = provider.IconKey,
            SupportsBringYourOwnKey = provider.SupportsBringYourOwnKey,
            SupportsManagedByIncoders = provider.SupportsManagedByIncoders,
            SupportsSystemDefault = provider.SupportsSystemDefault,
            SystemDefaultReadOnly = provider.SystemDefaultReadOnly,
            CredentialFieldsJson = SerializeCredentialFields(provider.CredentialFields),
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
}
