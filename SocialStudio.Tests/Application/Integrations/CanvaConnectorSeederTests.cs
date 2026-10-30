using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Integrations;
using SocialStudio.Tests.Common;

namespace SocialStudio.Tests.Application.Integrations;

/// <summary>
/// Pins the credential schema and idempotency contract of the Canva seed entry.
/// RED phase: all tests fail until WU-04 upgrades the seeder.
/// </summary>
public sealed class CanvaConnectorSeederTests
{
    private static readonly DateTime Now = new(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid CanvaGuid = new("b2610b54-a258-48d3-b2d2-12f70ac99d52");

    // ─── WU-01 — Seeder schema (CANVA-01, CANVA-04) ──────────────────────────

    [Fact]
    public async Task CanvaProvider_HasConnectApiByokCredentialSchema()
    {
        // Arrange
        var (seeder, providers) = BuildSeeder(Now);
        await seeder.SeedAsync();

        var canva = await providers.GetByKeyAsync("canva");
        Assert.NotNull(canva);

        // Assert exactly 3 fields
        Assert.Equal(3, canva.CredentialFields.Count);

        // client_id: secret, required
        var clientId = canva.CredentialFields.SingleOrDefault(f => f.Key == "client_id");
        Assert.NotNull(clientId);
        Assert.True(clientId!.IsSecret);
        Assert.True(clientId.IsRequired);

        // client_secret: secret, required
        var clientSecret = canva.CredentialFields.SingleOrDefault(f => f.Key == "client_secret");
        Assert.NotNull(clientSecret);
        Assert.True(clientSecret!.IsSecret);
        Assert.True(clientSecret.IsRequired);

        // scopes: non-secret, NOT required
        var scopes = canva.CredentialFields.SingleOrDefault(f => f.Key == "scopes");
        Assert.NotNull(scopes);
        Assert.False(scopes!.IsSecret);
        Assert.False(scopes.IsRequired);

        // Category and BYOK mode
        Assert.Equal(IntegrationProviderCategory.Design, canva.Category);
        Assert.True(canva.SupportsBringYourOwnKey);

        // Legacy access_token must be gone
        Assert.DoesNotContain(canva.CredentialFields, f => f.Key == "access_token");
    }

    [Fact]
    public async Task CanvaProvider_HasStableGuid()
    {
        // Arrange
        var (seeder, providers) = BuildSeeder(Now);
        await seeder.SeedAsync();

        var canva = await providers.GetByKeyAsync("canva");
        Assert.NotNull(canva);

        Assert.Equal(CanvaGuid, canva!.Id.Value);
    }

    [Fact]
    public async Task CanvaProvider_ScopesField_HasDefaultScopeStringInPlaceholderOrHelpText()
    {
        // Arrange
        var (seeder, providers) = BuildSeeder(Now);
        await seeder.SeedAsync();

        var canva = await providers.GetByKeyAsync("canva");
        Assert.NotNull(canva);

        var scopes = canva!.CredentialFields.SingleOrDefault(f => f.Key == "scopes");
        Assert.NotNull(scopes);

        var containsConnectWrite = (scopes!.Placeholder?.Contains("design:content:write", StringComparison.OrdinalIgnoreCase) == true)
                                   || (scopes.HelpText?.Contains("design:content:write", StringComparison.OrdinalIgnoreCase) == true);

        var containsProfileRead = (scopes.Placeholder?.Contains("profile:read", StringComparison.OrdinalIgnoreCase) == true)
                                  || (scopes.HelpText?.Contains("profile:read", StringComparison.OrdinalIgnoreCase) == true);

        Assert.True(containsConnectWrite, "scopes Placeholder or HelpText must contain 'design:content:write'");
        Assert.True(containsProfileRead, "scopes Placeholder or HelpText must contain 'profile:read'");
        Assert.False(scopes.IsRequired);
    }

    // ─── WU-02 — Idempotency (CANVA-02) ──────────────────────────────────────

    [Fact]
    public async Task SeedAsync_CalledTwice_ProducesExactlyOneCanvaRow()
    {
        // Arrange
        var (seeder, providers) = BuildSeeder(Now);

        // Act — seed twice
        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var allProviders = await providers.ListActiveAsync();
        var canvaRows = allProviders.Where(p => string.Equals(p.Key, "canva", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Single(canvaRows);
        Assert.Equal(CanvaGuid, canvaRows[0].Id.Value);
    }

    [Fact]
    public async Task SeedAsync_CalledTwice_CredentialSchemaIsUpdated()
    {
        // Arrange: insert a legacy canva provider (access_token only) before seeding
        var (seeder, providers) = BuildSeeder(Now);

        var legacyCanva = IntegrationProvider.Create(
            new IntegrationProviderId(CanvaGuid),
            "canva",
            "Canva",
            IntegrationProviderCategory.Design,
            null,
            "canva",
            supportsBringYourOwnKey: true,
            supportsManagedByIncoders: false,
            supportsSystemDefault: false,
            systemDefaultReadOnly: false,
            [new IntegrationCredentialField("access_token", "Access token", true, true)],
            [],
            Now);

        Assert.True(legacyCanva.IsSuccess);
        await providers.UpsertAsync(legacyCanva.Value);

        // Act: run SeedAsync once — it must overwrite the legacy schema
        await seeder.SeedAsync();

        var allProviders = await providers.ListActiveAsync();
        var canvaRows = allProviders.Where(p => string.Equals(p.Key, "canva", StringComparison.OrdinalIgnoreCase)).ToList();

        // Assert: exactly one row, new schema
        Assert.Single(canvaRows);
        var canva = canvaRows[0];
        Assert.Equal(CanvaGuid, canva.Id.Value);
        Assert.Equal(3, canva.CredentialFields.Count);
        Assert.DoesNotContain(canva.CredentialFields, f => f.Key == "access_token");
        Assert.Contains(canva.CredentialFields, f => f.Key == "client_id");
        Assert.Contains(canva.CredentialFields, f => f.Key == "client_secret");
        Assert.Contains(canva.CredentialFields, f => f.Key == "scopes");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static (IntegrationCatalogSeeder seeder, InMemorySeederProviderRepository providers) BuildSeeder(DateTime nowUtc)
    {
        var providers = new InMemorySeederProviderRepository();
        var unitOfWork = new InMemorySeederUnitOfWork();
        var clock = new TestClock(nowUtc);
        var seeder = new IntegrationCatalogSeeder(providers, unitOfWork, clock);
        return (seeder, providers);
    }

    // ── In-memory seeder harness ─────────────────────────────────────────────

    internal sealed class InMemorySeederProviderRepository : IIntegrationProviderCatalogRepository
    {
        private readonly Dictionary<string, IntegrationProvider> _byKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<IntegrationProviderId, IntegrationProvider> _byId = new();

        public Task<IReadOnlyList<IntegrationProvider>> ListActiveAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult<IReadOnlyList<IntegrationProvider>>(_byKey.Values.ToList());
        }

        public Task<IntegrationProvider?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            _byKey.TryGetValue(key, out var p);
            return Task.FromResult(p);
        }

        public Task<IntegrationProvider?> GetByIdAsync(IntegrationProviderId id, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            _byId.TryGetValue(id, out var p);
            return Task.FromResult(p);
        }

        public Task UpsertAsync(IntegrationProvider provider, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            _byKey[provider.Key] = provider;
            _byId[provider.Id] = provider;
            return Task.CompletedTask;
        }

        public bool TryGetCategory(IntegrationProviderId providerId, out IntegrationProviderCategory category)
        {
            if (_byId.TryGetValue(providerId, out var provider))
            {
                category = provider.Category;
                return true;
            }

            category = default;
            return false;
        }
    }

    private sealed class InMemorySeederUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(0);
        }
    }
}
