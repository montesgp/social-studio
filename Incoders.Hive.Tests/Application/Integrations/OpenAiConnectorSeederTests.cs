using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Integrations;

/// <summary>
/// Pins the OpenAI seed entry as a single shared connector that serves both the AI model and the
/// media generation categories, each exposing its own model menu.
/// </summary>
public sealed class OpenAiConnectorSeederTests
{
    private static readonly DateTime Now = new(2026, 6, 24, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task OpenAiProvider_ServesBothAiModelAndMediaGeneration_WithOneSharedCredential()
    {
        var (seeder, providers) = BuildSeeder(Now);
        await seeder.SeedAsync();

        var openai = await providers.GetByKeyAsync("openai");
        Assert.NotNull(openai);

        Assert.Contains(IntegrationProviderCategory.AiModel, openai!.Categories);
        Assert.Contains(IntegrationProviderCategory.MediaGeneration, openai.Categories);

        // One shared connector — a single api_key credential field, not duplicated per category.
        Assert.Single(openai.CredentialFields, f => f.Key == "api_key");
    }

    [Fact]
    public async Task OpenAiProvider_ExposesChatModelsForAiModel_AndImageModelsForMediaGeneration()
    {
        var (seeder, providers) = BuildSeeder(Now);
        await seeder.SeedAsync();

        var openai = await providers.GetByKeyAsync("openai");
        Assert.NotNull(openai);

        var chatModels = openai!.ModelsFor(IntegrationProviderCategory.AiModel);
        var imageModels = openai.ModelsFor(IntegrationProviderCategory.MediaGeneration);

        Assert.Contains("gpt-4.1", chatModels);
        Assert.DoesNotContain("dall-e-3", chatModels);

        Assert.Contains("gpt-image-1", imageModels);
        Assert.DoesNotContain("gpt-4.1", imageModels);
    }

    [Fact]
    public async Task SeedAsync_CalledTwice_ProducesExactlyOneOpenAiRow()
    {
        var (seeder, providers) = BuildSeeder(Now);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var all = await providers.ListActiveAsync();
        var openAiRows = all.Where(p => string.Equals(p.Key, "openai", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Single(openAiRows);
    }

    private static (IntegrationCatalogSeeder seeder, CanvaConnectorSeederTests.InMemorySeederProviderRepository providers) BuildSeeder(DateTime nowUtc)
    {
        var providers = new CanvaConnectorSeederTests.InMemorySeederProviderRepository();
        var unitOfWork = new InMemoryOpenAiSeederUnitOfWork();
        var clock = new TestClock(nowUtc);
        var seeder = new IntegrationCatalogSeeder(providers, unitOfWork, clock);
        return (seeder, providers);
    }

    private sealed class InMemoryOpenAiSeederUnitOfWork : Incoders.Hive.Application.Abstractions.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(0);
        }
    }
}
