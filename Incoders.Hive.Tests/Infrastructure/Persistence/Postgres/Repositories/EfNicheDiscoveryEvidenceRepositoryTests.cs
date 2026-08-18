using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>WU-7b: ND-11. Append-only, navigable evidence over <c>social.niche_discovery_evidence</c>.</summary>
public sealed class EfNicheDiscoveryEvidenceRepositoryTests
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _runId = Guid.NewGuid();
    private readonly Guid _opportunityId = Guid.NewGuid();
    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AppendAsync_ThenListByOpportunityAsync_RoundTrips()
    {
        await using var db = CreateDb();
        await Seed(db);
        var repo = new EfNicheDiscoveryEvidenceRepository(db);

        await repo.AppendAsync([Evidence("yt:video:1")], CancellationToken.None);

        var loaded = await repo.ListByOpportunityAsync(_orgId, _opportunityId, CancellationToken.None);

        Assert.Single(loaded);
        Assert.Equal("yt:video:1", loaded[0].ExternalRef);
        Assert.Equal(_opportunityId, loaded[0].OpportunityId);
        Assert.Null(loaded[0].Likes); // never zero-filled
    }

    /// <summary>
    /// The operator reviews an opportunity by hand, and "which country is this niche in" is the first
    /// question they ask. The provider reports it, so it must survive persistence rather than being
    /// captured and dropped.
    /// </summary>
    [Fact]
    public async Task AppendAsync_RoundTripsTheProviderReportedCountry()
    {
        await using var db = CreateDb();
        await Seed(db);
        var repo = new EfNicheDiscoveryEvidenceRepository(db);

        await repo.AppendAsync([Evidence("yt:video:1") with { Country = "ID" }], CancellationToken.None);

        var loaded = await repo.ListByOpportunityAsync(_orgId, _opportunityId, CancellationToken.None);

        Assert.Equal("ID", loaded[0].Country);
    }

    /// <summary>An unreported country stays absent — never defaulted to the run's market (ND-05).</summary>
    [Fact]
    public async Task AppendAsync_KeepsAnUnreportedCountryNull()
    {
        await using var db = CreateDb();
        await Seed(db);
        var repo = new EfNicheDiscoveryEvidenceRepository(db);

        await repo.AppendAsync([Evidence("yt:video:1")], CancellationToken.None);

        var loaded = await repo.ListByOpportunityAsync(_orgId, _opportunityId, CancellationToken.None);

        Assert.Null(loaded[0].Country);
    }

    /// <summary>
    /// REGRESSION (run e6124e47, 2026-08-15): one channel legitimately backs SEVERAL opportunities in
    /// the same run — it publishes into more than one cluster. Evidence is per-opportunity by design
    /// (<see cref="EfNicheDiscoveryEvidenceRepository.ListByOpportunityAsync"/>), so the same channel
    /// under two opportunities is two distinct rows, not a duplicate.
    ///
    /// Treating it as a duplicate threw mid-append, which left the run stuck in Running forever with
    /// 387 opportunities persisted and zero evidence behind them.
    /// </summary>
    [Fact]
    public async Task AppendAsync_SameChannelUnderTwoOpportunities_IsNotADuplicate()
    {
        await using var db = CreateDb();
        await Seed(db);
        var secondOpportunityId = await SeedSecondOpportunity(db);
        var repo = new EfNicheDiscoveryEvidenceRepository(db);

        await repo.AppendAsync(
            [
                Evidence("yt:channel:1") with { EvidenceKind = NicheDiscoveryEvidenceKind.Channel },
                Evidence("yt:channel:1") with
                {
                    Id = Guid.NewGuid(),
                    EvidenceKind = NicheDiscoveryEvidenceKind.Channel,
                    OpportunityId = secondOpportunityId,
                },
            ],
            CancellationToken.None);

        Assert.Single(await repo.ListByOpportunityAsync(_orgId, _opportunityId, CancellationToken.None));
        Assert.Single(await repo.ListByOpportunityAsync(_orgId, secondOpportunityId, CancellationToken.None));
    }

    /// <summary>The same ref twice under the SAME opportunity is still a genuine duplicate.</summary>
    [Fact]
    public async Task AppendAsync_SameRefTwiceUnderOneOpportunity_IsStillRejected()
    {
        await using var db = CreateDb();
        await Seed(db);
        var repo = new EfNicheDiscoveryEvidenceRepository(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.AppendAsync(
                [Evidence("yt:video:1"), Evidence("yt:video:1") with { Id = Guid.NewGuid() }],
                CancellationToken.None));
    }

    [Fact]
    public async Task AppendAsync_DoubleWriteOfTheSameRunKindRef_IsRejected()
    {
        await using var db = CreateDb();
        await Seed(db);
        var repo = new EfNicheDiscoveryEvidenceRepository(db);

        await repo.AppendAsync([Evidence("yt:video:1")], CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.AppendAsync([Evidence("yt:video:1")], CancellationToken.None));
    }

    [Fact]
    public async Task ListByOpportunityAsync_NavigatesBackToTheOwningOpportunity()
    {
        await using var db = CreateDb();
        await Seed(db);
        var repo = new EfNicheDiscoveryEvidenceRepository(db);
        await repo.AppendAsync([Evidence("yt:video:1"), Evidence("yt:video:2")], CancellationToken.None);

        var loaded = await repo.ListByOpportunityAsync(_orgId, _opportunityId, CancellationToken.None);

        Assert.Equal(2, loaded.Count);
        Assert.All(loaded, e => Assert.Equal(_opportunityId, e.OpportunityId));
    }

    private NicheDiscoveryEvidenceRecord Evidence(string externalRef) => new(
        Id: Guid.NewGuid(),
        RunId: _runId,
        OpportunityId: _opportunityId,
        OrganizationId: _orgId,
        EvidenceKind: NicheDiscoveryEvidenceKind.Video,
        ExternalRef: externalRef,
        ChannelHandle: "@creator",
        Title: "A tutorial",
        ThumbnailUrl: null,
        Description: null,
        PublishedAtUtc: ObservedAt.AddDays(-3),
        Views: 12_000,
        Likes: null,
        Comments: null,
        SubscriberCount: null,
        TotalViews: null,
        VideoCount: null,
        DurationSeconds: 480,
        LanguageCode: "es",
        Country: null,
        SourceKey: "youtube-data-api",
        ObservedAtUtc: ObservedAt);

    private async Task<Guid> SeedSecondOpportunity(AppIdentityDbContext db)
    {
        var id = Guid.NewGuid();

        db.NicheOpportunities.Add(new NicheOpportunityData
        {
            Id = id,
            RunId = _runId,
            OrganizationId = _orgId,
            ContentPattern = "tutorial",
            TopicLabels = [],
            OpportunityScore = 0.4,
            ConfidenceScore = 0.4,
            EffectiveWeightDenominator = 0.9,
            ExcludedSignalsJson = "[]",
            ChannelSampleSize = 1,
            VideoSampleSize = 1,
            Rank = 2,
            ObservedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        return id;
    }

    private async Task Seed(AppIdentityDbContext db)
    {
        db.Tenants.Add(new TenantData
        {
            Id = _orgId,
            Name = "org",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        db.DiscoveryRuns.Add(new DiscoveryRunData
        {
            Id = _runId,
            OrganizationId = _orgId,
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

        db.NicheOpportunities.Add(new NicheOpportunityData
        {
            Id = _opportunityId,
            RunId = _runId,
            OrganizationId = _orgId,
            ContentPattern = "tutorial",
            TopicLabels = [],
            OpportunityScore = 0.5,
            ConfidenceScore = 0.5,
            EffectiveWeightDenominator = 0.9,
            ExcludedSignalsJson = "[]",
            ChannelSampleSize = 1,
            VideoSampleSize = 1,
            Rank = 1,
            ObservedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    private static AppIdentityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"niche-evidence-{Guid.NewGuid():N}")
            .Options;

        return new AppIdentityDbContext(options);
    }
}
