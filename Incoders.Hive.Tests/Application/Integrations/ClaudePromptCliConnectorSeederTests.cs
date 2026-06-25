using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Integrations;

public sealed class ClaudePromptCliConnectorSeederTests
{
    private static readonly DateTime Now = new(2026, 6, 24, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ClaudePromptProvider_ServesAiModelAndMediaGeneration_WithSafeCredentialSchema()
    {
        var providers = new CanvaConnectorSeederTests.InMemorySeederProviderRepository();
        var seeder = new IntegrationCatalogSeeder(providers, new UnitOfWork(), new TestClock(Now));

        await seeder.SeedAsync();

        var provider = await providers.GetByKeyAsync("claude-p");
        Assert.NotNull(provider);
        Assert.Contains(IntegrationProviderCategory.AiModel, provider!.Categories);
        Assert.Contains(IntegrationProviderCategory.MediaGeneration, provider.Categories);
        Assert.Contains(provider.CredentialFields, f => f.Key == "executable_path" && !f.IsSecret);
        Assert.Contains(provider.CredentialFields, f => f.Key == "allowed_output_directory" && !f.IsSecret);
        Assert.Contains(provider.CredentialFields, f => f.Key == "timeout_ms" && !f.IsSecret);
        Assert.Contains(provider.CredentialFields, f => f.Key == "settings_json" && f.IsSecret);
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
