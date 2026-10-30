using SocialStudio.Application.Abstractions.Social.NicheDiscovery;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using SocialStudio.Infrastructure.Persistence.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// WU-7b: ND-11, ND-16. Append-only read model over <c>social.niche_opportunities</c>.
/// </summary>
public sealed class EfNicheOpportunityRepositoryTests
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _runId = Guid.NewGuid();
    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AppendAsync_ThenListByRunAsync_RoundTrips()
    {
        await using var db = CreateDb();
        await SeedRun(db, _orgId, _runId);
        var repo = new EfNicheOpportunityRepository(db);

        await repo.AppendAsync([Record(_orgId, _runId, rank: 1)], CancellationToken.None);

        var loaded = await repo.ListByRunAsync(_orgId, _runId, CancellationToken.None);

        Assert.Single(loaded);
        Assert.Equal(1, loaded[0].Rank);
        Assert.Equal(0.75, loaded[0].OpportunityScore);
        Assert.Null(loaded[0].AutomationSuitability); // never zero-filled
    }

    [Fact]
    public async Task AppendAsync_DoubleWriteOfTheSameRankInsideARun_IsAHardError()
    {
        await using var db = CreateDb();
        await SeedRun(db, _orgId, _runId);
        var repo = new EfNicheOpportunityRepository(db);

        await repo.AppendAsync([Record(_orgId, _runId, rank: 1)], CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.AppendAsync([Record(_orgId, _runId, rank: 1)], CancellationToken.None));
    }

    [Fact]
    public async Task Port_ExposesNoUpdateOrDeleteMethod()
    {
        var members = typeof(INicheOpportunityRepository).GetMethods().Select(m => m.Name).ToList();

        Assert.DoesNotContain(members, name => name.Contains("Update", StringComparison.Ordinal));
        Assert.DoesNotContain(members, name => name.Contains("Delete", StringComparison.Ordinal));
        Assert.DoesNotContain(members, name => name.Contains("Remove", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ALaterRun_AppendsNewRows_EarlierRunRowsAreUnchanged()
    {
        await using var db = CreateDb();
        await SeedRun(db, _orgId, _runId);
        var secondRunId = Guid.NewGuid();
        await SeedRun(db, _orgId, secondRunId);
        var repo = new EfNicheOpportunityRepository(db);

        await repo.AppendAsync([Record(_orgId, _runId, rank: 1)], CancellationToken.None);
        await repo.AppendAsync([Record(_orgId, secondRunId, rank: 1)], CancellationToken.None);

        var firstRun = await repo.ListByRunAsync(_orgId, _runId, CancellationToken.None);
        var secondRun = await repo.ListByRunAsync(_orgId, secondRunId, CancellationToken.None);

        Assert.Single(firstRun);
        Assert.Single(secondRun);
    }

    [Fact]
    public async Task CascadeDeleteFromTheOrganization_RemovesOpportunities()
    {
        await using var db = CreateDb();
        await SeedRun(db, _orgId, _runId);
        var repo = new EfNicheOpportunityRepository(db);
        await repo.AppendAsync([Record(_orgId, _runId, rank: 1)], CancellationToken.None);

        var org = await db.Tenants.SingleAsync(x => x.Id == _orgId);
        db.Tenants.Remove(org);
        await db.SaveChangesAsync();

        var remaining = await db.NicheOpportunities.CountAsync(x => x.OrganizationId == _orgId);
        Assert.Equal(0, remaining);
    }

    private static NicheOpportunityRecord Record(Guid orgId, Guid runId, int rank) => new(
        Id: Guid.NewGuid(),
        RunId: runId,
        OrganizationId: orgId,
        NicheId: null,
        SubNicheLabel: "creator-economy-tools",
        ContentPattern: "tutorial",
        TopicLabels: ["ai", "automation"],
        OpportunityScore: 0.75,
        ConfidenceScore: 0.60,
        EffectiveWeightDenominator: 0.90,
        ExcludedSignalsJson: "[]",
        MedianViews: 50_000,
        OutlierRatio: 3.2,
        OutlierDensity: 0.4,
        GrowthProxy: 1.5,
        CrossChannelRepetition: 5,
        TargetMarketDemand: 0.6,
        TargetMarketCompetition: 0.3,
        TargetMarketGap: 0.42,
        TargetMarketRawDemand: 1250.0,
        AutomationSuitability: null,
        ChannelSampleSize: 12,
        VideoSampleSize: 40,
        Rank: rank,
        ObservedAtUtc: ObservedAt);

    private async Task SeedRun(AppIdentityDbContext db, Guid orgId, Guid runId)
    {
        if (!await db.Tenants.AnyAsync(x => x.Id == orgId))
        {
            db.Tenants.Add(new TenantData
            {
                Id = orgId,
                Name = "org",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }

        db.DiscoveryRuns.Add(new DiscoveryRunData
        {
            Id = runId,
            OrganizationId = orgId,
            RequestedByUserId = Guid.NewGuid(),
            SourceMarketCode = "es-AR",
            TargetMarketCode = "es-AR",
            NetworkKey = "youtube",
            ContentFormat = "video",
            ConfigurationSnapshotJson = "{}",
            WindowDays = 90,
            Depth = 2,
            Status = 1,
            WeightsJson = "{}",
            OriginSourceKeys = [],
            UnconfiguredSourceKeys = [],
            FailedSourceKeys = [],
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    private static AppIdentityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"niche-opportunities-{Guid.NewGuid():N}")
            .Options;

        return new AppIdentityDbContext(options);
    }
}
