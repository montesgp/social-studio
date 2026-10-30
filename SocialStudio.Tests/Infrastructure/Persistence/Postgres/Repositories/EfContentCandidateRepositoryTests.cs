using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// WU-5: CD-08. Round-trip for the append-only observation table, and the "double-write inside one
/// run" guarantee (design §5.1's unique index) enforced at the repository boundary.
/// </summary>
public sealed class EfContentCandidateRepositoryTests
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _otherOrgId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();
    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordObservationsAsync_RoundTripsTheSnapshot()
    {
        await using var db = CreateDb();
        var repo = new EfContentCandidateRepository(db);

        await repo.RecordObservationsAsync(_orgId, _clientId, [Observation("tiktok:https://tiktok.com/@a/video/1")], CancellationToken.None);

        var loaded = await repo.GetLatestByKeysAsync(_orgId, _clientId, ["tiktok:https://tiktok.com/@a/video/1"], CancellationToken.None);

        Assert.Single(loaded);
        Assert.Equal(5_000, loaded[0].Views);
        Assert.Null(loaded[0].Shares); // never zero-filled
    }

    [Fact]
    public async Task RecordObservationsAsync_DoubleWrite_SameKeyAndObservedAt_InsideOneRun_IsNotDuplicated()
    {
        await using var db = CreateDb();
        var repo = new EfContentCandidateRepository(db);
        var duplicate = Observation("tiktok:https://tiktok.com/@a/video/1");

        await repo.RecordObservationsAsync(_orgId, _clientId, [duplicate, duplicate], CancellationToken.None);

        var rowCount = await db.DiscoveredCandidates.CountAsync();
        Assert.Equal(1, rowCount);
    }

    [Fact]
    public async Task SameCandidateObservedInALaterRun_CreatesANewObservation_NotADuplicateIdentity()
    {
        await using var db = CreateDb();
        var repo = new EfContentCandidateRepository(db);
        var key = "tiktok:https://tiktok.com/@a/video/1";

        await repo.RecordObservationsAsync(_orgId, _clientId, [Observation(key, ObservedAt)], CancellationToken.None);
        await repo.RecordObservationsAsync(_orgId, _clientId, [Observation(key, ObservedAt.AddDays(7))], CancellationToken.None);

        var rowCount = await db.DiscoveredCandidates.CountAsync();
        Assert.Equal(2, rowCount);

        var latest = await repo.GetLatestByKeysAsync(_orgId, _clientId, [key], CancellationToken.None);
        Assert.Single(latest);
        Assert.Equal(ObservedAt.AddDays(7), latest[0].ObservedAtUtc);
    }

    [Fact]
    public async Task GetLatestByKeysAsync_IsTenantScoped()
    {
        await using var db = CreateDb();
        var repo = new EfContentCandidateRepository(db);
        var key = "tiktok:https://tiktok.com/@a/video/1";

        await repo.RecordObservationsAsync(_orgId, _clientId, [Observation(key)], CancellationToken.None);

        var crossTenant = await repo.GetLatestByKeysAsync(_otherOrgId, _clientId, [key], CancellationToken.None);

        Assert.Empty(crossTenant);
    }

    private static CandidateObservation Observation(string candidateKey, DateTimeOffset? observedAt = null) =>
        new(
            CandidateKey: candidateKey,
            NetworkKey: "tiktok",
            SourceKey: "niche",
            SearchCriterion: "niche",
            ContentUrl: "https://tiktok.com/@a/video/1",
            AuthorHandle: "@a",
            Title: "t",
            Summary: "s",
            ThumbnailUrl: null,
            PublishedAtUtc: ObservedAt.AddDays(-1),
            Views: 5_000,
            Likes: 100,
            Comments: 10,
            Shares: null,
            Saves: null,
            OpportunityScore: 0.7,
            Classification: 1,
            ObservedAtUtc: observedAt ?? ObservedAt);

    private static AppIdentityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"content-candidates-{Guid.NewGuid():N}")
            .Options;

        return new AppIdentityDbContext(options);
    }
}
