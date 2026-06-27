using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Integrations;

public sealed class ClaudePromptCliConnectorSeederTests
{
    private static readonly DateTime Now = new(2026, 6, 24, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ClaudePromptProvider_ServesAiModelAndMediaGeneration_WithZeroConfigCredentialSchema()
    {
        var providers = new CanvaConnectorSeederTests.InMemorySeederProviderRepository();
        var seeder = new IntegrationCatalogSeeder(providers, new UnitOfWork(), new TestClock(Now));

        await seeder.SeedAsync();

        var provider = await providers.GetByKeyAsync("claude-p");
        Assert.NotNull(provider);
        Assert.Contains(IntegrationProviderCategory.AiModel, provider!.Categories);
        Assert.Contains(IntegrationProviderCategory.MediaGeneration, provider.Categories);
        // Host-local provider: no per-organization credential fields. Host tuning falls back to the
        // safe defaults in ClaudePromptCliSettings, so the connector is connectable with zero config.
        Assert.Empty(provider.CredentialFields);
    }

    [Fact]
    public async Task ClaudePromptProvider_SupportsZeroConfigConnectionModes()
    {
        var providers = new CanvaConnectorSeederTests.InMemorySeederProviderRepository();
        var seeder = new IntegrationCatalogSeeder(providers, new UnitOfWork(), new TestClock(Now));

        await seeder.SeedAsync();

        var provider = await providers.GetByKeyAsync("claude-p");
        Assert.NotNull(provider);
        Assert.True(provider!.SupportsBringYourOwnKey);
        Assert.True(provider.SupportsManagedByIncoders);
        Assert.True(provider.SupportsSystemDefault);
        Assert.False(provider.SystemDefaultReadOnly);
    }

    [Fact]
    public async Task ClaudePromptProvider_ExposesDifferentTextAndMediaModelMenus()
    {
        var providers = new CanvaConnectorSeederTests.InMemorySeederProviderRepository();
        var seeder = new IntegrationCatalogSeeder(providers, new UnitOfWork(), new TestClock(Now));

        await seeder.SeedAsync();

        var provider = await providers.GetByKeyAsync("claude-p");
        Assert.NotNull(provider);
        Assert.Contains("claude-sonnet-4-6", provider!.ModelsFor(IntegrationProviderCategory.AiModel));
        Assert.Contains("claude-p-image-json", provider.ModelsFor(IntegrationProviderCategory.MediaGeneration));
    }

    private sealed class UnitOfWork : Incoders.Hive.Application.Abstractions.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
