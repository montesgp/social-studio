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
        PublishedAtUtc: ObservedAt.AddDays(-3),
        Views: 12_000,
        Likes: null,
        Comments: null,
        SubscriberCount: null,
        TotalViews: null,
        VideoCount: null,
        DurationSeconds: 480,
        LanguageCode: "es",
        SourceKey: "youtube-data-api",
        ObservedAtUtc: ObservedAt);

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
