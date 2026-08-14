using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// The niche candidate BANK: the reserve a niche search serves from instead of re-scraping.
///
/// It is scoped by ORGANIZATION + NETWORK + HASHTAG and deliberately NOT by client — two clients of the
/// same organization working the same niche must share one paid scrape, never buy it twice.
/// </summary>
public sealed class EfContentCandidateRepositoryBankTests
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _otherOrgId = Guid.NewGuid();
    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordNicheBankAsync_ThenGetNicheBankAsync_RoundTripsTheCandidate()
    {
        await using var db = CreateDb();
        var repo = new EfContentCandidateRepository(db);

        await repo.RecordNicheBankAsync(_orgId, "tiktok", ["medical", "health"], [Observation("k1")], CancellationToken.None);

        var banked = await repo.GetNicheBankAsync(_orgId, "tiktok", ["medical"], 20, CancellationToken.None);

        Assert.Single(banked);
        Assert.Equal("k1", banked[0].CandidateKey);
        Assert.Equal(5_000, banked[0].Views);
        Assert.Null(banked[0].Shares);
        Assert.Equal(ObservedAt, banked[0].ObservedAtUtc);
    }

    [Fact]
    public async Task GetNicheBankAsync_MatchesAnyOverlappingHashtag_NotOnlyTheFirst()
    {
        await using var db = CreateDb();
        var repo = new EfContentCandidateRepository(db);

        // Client A's niche "Medical & health" derives three tokens; the run is banked under all of them.
        await repo.RecordNicheBankAsync(_orgId, "tiktok", ["medical", "health", "medicalhealth"], [Observation("k1")], CancellationToken.None);

        // Client B's niche is simply "Health" — one token, and it overlaps.
        var banked = await repo.GetNicheBankAsync(_orgId, "tiktok", ["health"], 20, CancellationToken.None);

        Assert.Single(banked);
    }

    [Fact]
    public async Task GetNicheBankAsync_NoOverlappingHashtag_ReturnsEmpty()
    {
        await using var db = CreateDb();
        var repo = new EfContentCandidateRepository(db);

        await repo.RecordNicheBankAsync(_orgId, "tiktok", ["medical"], [Observation("k1")], CancellationToken.None);

        var banked = await repo.GetNicheBankAsync(_orgId, "tiktok", ["carpentry"], 20, CancellationToken.None);

        Assert.Empty(banked);
    }

    [Fact]
    public async Task GetNicheBankAsync_IsScopedByOrganization()
    {
        await using var db = CreateDb();
        var repo = new EfContentCandidateRepository(db);

        await repo.RecordNicheBankAsync(_orgId, "tiktok", ["medical"], [Observation("k1")], CancellationToken.None);

        var banked = await repo.GetNicheBankAsync(_otherOrgId, "tiktok", ["medical"], 20, CancellationToken.None);

        Assert.Empty(banked);
    }

    [Fact]
    public async Task GetNicheBankAsync_IsScopedByNetwork()
    {
        await using var db = CreateDb();
        var repo = new EfContentCandidateRepository(db);

        await repo.RecordNicheBankAsync(_orgId, "tiktok", ["medical"], [Observation("k1")], CancellationToken.None);

        var banked = await repo.GetNicheBankAsync(_orgId, "instagram", ["medical"], 20, CancellationToken.None);

        Assert.Empty(banked);
    }

    [Fact]
    public async Task RecordNicheBankAsync_ReObservingAStoredCandidate_DoesNotDuplicateIt()
    {
        await using var db = CreateDb();
        var repo = new EfContentCandidateRepository(db);

        await repo.RecordNicheBankAsync(_orgId, "tiktok", ["medical"], [Observation("k1")], CancellationToken.None);
        await repo.RecordNicheBankAsync(
            _orgId,
            "tiktok",
            ["medical"],
            [Observation("k1", ObservedAt.AddDays(3))],
            CancellationToken.None);

        Assert.Equal(1, await db.DiscoveredCandidates.CountAsync());

        var banked = await repo.GetNicheBankAsync(_orgId, "tiktok", ["medical"], 20, CancellationToken.None);
        Assert.Single(banked);
        // The stored snapshot is the ORIGINAL observation: the bank never silently rewrites what it
        // already paid for, so the freshness the operator is shown stays true.
        Assert.Equal(ObservedAt, banked[0].ObservedAtUtc);
    }

    [Fact]
    public async Task RecordNicheBankAsync_ReObservingUnderANewHashtag_WidensReachWithoutADuplicateRow()
    {
        await using var db = CreateDb();
        var repo = new EfContentCandidateRepository(db);

        await repo.RecordNicheBankAsync(_orgId, "tiktok", ["medical"], [Observation("k1")], CancellationToken.None);
        await repo.RecordNicheBankAsync(_orgId, "tiktok", ["health"], [Observation("k1")], CancellationToken.None);

        Assert.Equal(1, await db.DiscoveredCandidates.CountAsync());
        Assert.Single(await repo.GetNicheBankAsync(_orgId, "tiktok", ["medical"], 20, CancellationToken.None));
        Assert.Single(await repo.GetNicheBankAsync(_orgId, "tiktok", ["health"], 20, CancellationToken.None));
    }

    [Fact]
    public async Task GetNicheBankAsync_HonoursTheResultBudget_StrongestFirst()
    {
        await using var db = CreateDb();
        var repo = new EfContentCandidateRepository(db);

        await repo.RecordNicheBankAsync(
            _orgId,
            "tiktok",
            ["medical"],
            [
                Observation("k1") with { Views = 10 },
                Observation("k2") with { Views = 900 },
                Observation("k3") with { Views = 500 },
            ],
            CancellationToken.None);

        var banked = await repo.GetNicheBankAsync(_orgId, "tiktok", ["medical"], 2, CancellationToken.None);

        Assert.Equal(2, banked.Count);
        Assert.Equal("k2", banked[0].CandidateKey);
        Assert.Equal("k3", banked[1].CandidateKey);
    }

    [Fact]
    public async Task BankRows_AreNotReturnedByTheClientScopedObservationRead()
    {
        await using var db = CreateDb();
        var repo = new EfContentCandidateRepository(db);
        var clientId = Guid.NewGuid();

        await repo.RecordNicheBankAsync(_orgId, "tiktok", ["medical"], [Observation("k1")], CancellationToken.None);

        // The bank is org-scoped; it must never leak into the per-client observation history.
        var perClient = await repo.GetLatestByKeysAsync(_orgId, clientId, ["k1"], CancellationToken.None);

        Assert.Empty(perClient);
    }

    private static CandidateObservation Observation(string candidateKey, DateTimeOffset? observedAt = null) =>
        new(
            CandidateKey: candidateKey,
            NetworkKey: "tiktok",
            SourceKey: "niche",
            SearchCriterion: "niche",
            ContentUrl: $"https://tiktok.com/@a/video/{candidateKey}",
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
            .UseInMemoryDatabase($"content-candidate-bank-{Guid.NewGuid():N}")
            .Options;

        return new AppIdentityDbContext(options);
    }
}
