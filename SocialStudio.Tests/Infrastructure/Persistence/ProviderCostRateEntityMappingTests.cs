using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence;

public sealed class ProviderCostRateEntityMappingTests
{
    private static readonly DateTime Now = new(2026, 5, 26, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ProviderCostRateData_RoundTrip_AllFieldsPersistCorrectly()
    {
        await using var db = CreateDbContext();
        var entity = new ProviderCostRateData
        {
            Id = Guid.NewGuid(),
            ProviderKey = "claude",
            Model = "claude-sonnet-4-5",
            CostPer1KInputTokens = 0.00300000m,
            CostPer1KOutputTokens = 0.01500000m,
            CostPerRequest = null,
            EffectiveFrom = Now.AddDays(-30),
            EffectiveTo = null,
            Notes = "v1 seed; verify pricing before production",
        };

        db.ProviderCostRates.Add(entity);
        await db.SaveChangesAsync();

        var read = await db.ProviderCostRates.FindAsync(entity.Id);
        Assert.NotNull(read);
        Assert.Equal(entity.ProviderKey, read!.ProviderKey);
        Assert.Equal(entity.Model, read.Model);
        Assert.Equal(entity.CostPer1KInputTokens, read.CostPer1KInputTokens);
        Assert.Equal(entity.CostPer1KOutputTokens, read.CostPer1KOutputTokens);
        Assert.Null(read.CostPerRequest);
        Assert.Null(read.EffectiveTo);
        Assert.Equal(entity.Notes, read.Notes);
    }

    [Fact]
    public async Task ProviderCostRateData_ProviderWithoutModel_NullModelAllowed()
    {
        await using var db = CreateDbContext();
        var entity = new ProviderCostRateData
        {
            Id = Guid.NewGuid(),
            ProviderKey = "apify",
            Model = null,
            CostPer1KInputTokens = null,
            CostPer1KOutputTokens = null,
            CostPerRequest = 0.00500000m,
            EffectiveFrom = Now,
            EffectiveTo = null,
            Notes = "v1 seed; verify pricing before production",
        };

        db.ProviderCostRates.Add(entity);
        await db.SaveChangesAsync();

        var read = await db.ProviderCostRates.FindAsync(entity.Id);
        Assert.NotNull(read);
        Assert.Null(read!.Model);
        Assert.Null(read.CostPer1KInputTokens);
        Assert.Equal(0.00500000m, read.CostPerRequest);
    }

    [Fact]
    public async Task ProviderCostRateData_WithEffectiveTo_ExpiryDatePersisted()
    {
        await using var db = CreateDbContext();
        var expiry = Now.AddDays(90);
        var entity = new ProviderCostRateData
        {
            Id = Guid.NewGuid(),
            ProviderKey = "openai",
            Model = "gpt-4o",
            CostPer1KInputTokens = 0.00250000m,
            CostPer1KOutputTokens = 0.01000000m,
            CostPerRequest = null,
            EffectiveFrom = Now,
            EffectiveTo = expiry,
            Notes = null,
        };

        db.ProviderCostRates.Add(entity);
        await db.SaveChangesAsync();

        var read = await db.ProviderCostRates.FindAsync(entity.Id);
        Assert.NotNull(read);
        Assert.Equal(expiry, read!.EffectiveTo);
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"cost-rates-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
