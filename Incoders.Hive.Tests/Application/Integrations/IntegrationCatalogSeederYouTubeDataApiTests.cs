using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Integrations;

/// <summary>
/// WU-4 (INT-ND-01, INT-ND-02): <c>youtube-data-api-v3</c> seeds as a distinct provider from the
/// existing <c>youtube</c> publishing/scrape row, under the new
/// <see cref="IntegrationProviderCategory.OfficialDataApi"/> category, with a single API-key
/// credential schema and no <c>actor_mappings_json</c> entry.
/// </summary>
public sealed class IntegrationCatalogSeederYouTubeDataApiTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task YouTubeDataApiV3_SeedsAsADistinctProvider_FromTheExistingYouTubePublishingRow()
    {
        var (seeder, providers) = BuildSeeder(Now);
        await seeder.SeedAsync();

        var dataApi = await providers.GetByKeyAsync("youtube-data-api-v3");
        var publishing = await providers.GetByKeyAsync("youtube");

        Assert.NotNull(dataApi);
        Assert.NotNull(publishing);
        Assert.NotEqual(dataApi!.Id, publishing!.Id);
    }

    [Fact]
    public async Task YouTubeDataApiV3_IsCategorizedAsOfficialDataApi()
    {
        var (seeder, providers) = BuildSeeder(Now);
        await seeder.SeedAsync();

        var dataApi = await providers.GetByKeyAsync("youtube-data-api-v3");

        Assert.NotNull(dataApi);
        Assert.Equal(IntegrationProviderCategory.OfficialDataApi, dataApi!.Category);
    }

    [Fact]
    public async Task YouTubeDataApiV3_CredentialSchema_IsASingleApiKeyField_WithNoActorMappingsEntry()
    {
        var (seeder, providers) = BuildSeeder(Now);
        await seeder.SeedAsync();

        var dataApi = await providers.GetByKeyAsync("youtube-data-api-v3");

        Assert.NotNull(dataApi);
        var field = Assert.Single(dataApi!.CredentialFields);
        Assert.Equal("api_key", field.Key);
        Assert.True(field.IsSecret);
        Assert.True(field.IsRequired);
        Assert.DoesNotContain(dataApi.CredentialFields, f => f.Key == "actor_mappings_json");
    }

    private static (IntegrationCatalogSeeder seeder, CanvaConnectorSeederTests.InMemorySeederProviderRepository providers) BuildSeeder(DateTime nowUtc)
    {
        var providers = new CanvaConnectorSeederTests.InMemorySeederProviderRepository();
        var unitOfWork = new InMemoryYouTubeDataApiSeederUnitOfWork();
        var clock = new TestClock(nowUtc);
        var seeder = new IntegrationCatalogSeeder(providers, unitOfWork, clock);
        return (seeder, providers);
    }

    private sealed class InMemoryYouTubeDataApiSeederUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(0);
        }
    }
}
