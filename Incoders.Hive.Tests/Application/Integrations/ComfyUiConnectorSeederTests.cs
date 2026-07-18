using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Integrations;

/// <summary>
/// Resolves the open question from the ComfyUI sampler-settings change: the comfyui provider row
/// already exists in the dev DB with only <c>base_url</c> declared. These tests pin that
/// <see cref="IntegrationCatalogSeeder.SeedAsync"/> is an UPSERT keyed by provider <c>Key</c>
/// (mirrors <c>CanvaConnectorSeederTests.SeedAsync_CalledTwice_CredentialSchemaIsUpdated</c>) — a
/// row seeded before <c>steps</c>/<c>cfg</c> were added still picks them up on the next
/// application startup, no backfill/migration required.
/// </summary>
public sealed class ComfyUiConnectorSeederTests
{
    private static readonly DateTime Now = new(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ComfyUiGuid = new("6a1f2c3d-9b4e-4f7a-8c2d-1e5f6a7b8c07");

    [Fact]
    public async Task SeedAsync_ExistingRowWithOnlyBaseUrl_PicksUpStepsAndCfgFieldsOnNextStartup()
    {
        var (seeder, providers) = BuildSeeder(Now);

        // Arrange: simulate the already-seeded dev DB row — comfyui with ONLY base_url declared,
        // as it existed before this change.
        var legacyComfyUi = IntegrationProvider.Create(
            new IntegrationProviderId(ComfyUiGuid),
            "comfyui",
            "ComfyUI",
            IntegrationProviderCategory.MediaGeneration,
            null,
            "comfyui",
            supportsBringYourOwnKey: false,
            supportsManagedByIncoders: false,
            supportsSystemDefault: true,
            systemDefaultReadOnly: false,
            [new IntegrationCredentialField("base_url", "Base URL", false, false, "http://127.0.0.1:8188", "ComfyUI server base URL")],
            [],
            Now);

        Assert.True(legacyComfyUi.IsSuccess);
        await providers.UpsertAsync(legacyComfyUi.Value);

        // Act: startup re-runs SeedAsync (Program.cs: SeedIntegrationCatalogAsync runs every boot).
        await seeder.SeedAsync();

        // Assert: the SAME row (same id) now carries steps + cfg alongside base_url — no duplicate
        // row, no manual backfill needed.
        var allProviders = await providers.ListActiveAsync();
        var comfyUiRows = allProviders.Where(p => string.Equals(p.Key, "comfyui", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Single(comfyUiRows);
        var comfyui = comfyUiRows[0];
        Assert.Equal(ComfyUiGuid, comfyui.Id.Value);
        Assert.Contains(comfyui.CredentialFields, f => f.Key == "base_url");
        Assert.Contains(comfyui.CredentialFields, f => f.Key == "steps");
        Assert.Contains(comfyui.CredentialFields, f => f.Key == "cfg");
    }

    [Fact]
    public async Task ComfyUiProvider_StepsAndCfgFields_AreOptionalAndNotSecret()
    {
        var (seeder, providers) = BuildSeeder(Now);
        await seeder.SeedAsync();

        var comfyui = await providers.GetByKeyAsync("comfyui");
        Assert.NotNull(comfyui);

        var steps = comfyui!.CredentialFields.Single(f => f.Key == "steps");
        Assert.False(steps.IsRequired);
        Assert.False(steps.IsSecret);

        var cfg = comfyui.CredentialFields.Single(f => f.Key == "cfg");
        Assert.False(cfg.IsRequired);
        Assert.False(cfg.IsSecret);
    }

    private static (IntegrationCatalogSeeder seeder, CanvaConnectorSeederTests.InMemorySeederProviderRepository providers) BuildSeeder(DateTime nowUtc)
    {
        var providers = new CanvaConnectorSeederTests.InMemorySeederProviderRepository();
        var unitOfWork = new InMemoryComfyUiSeederUnitOfWork();
        var clock = new TestClock(nowUtc);
        var seeder = new IntegrationCatalogSeeder(providers, unitOfWork, clock);
        return (seeder, providers);
    }

    private sealed class InMemoryComfyUiSeederUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(0);
        }
    }
}
