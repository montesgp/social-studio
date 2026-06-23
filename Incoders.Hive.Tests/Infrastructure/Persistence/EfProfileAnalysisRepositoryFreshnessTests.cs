using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

/// <summary>
/// Integration tests for the freshness/debounce methods on EfProfileAnalysisRepository.
/// Uses an in-memory EF context — no real Postgres connection needed.
/// Refs: CAG-PA-04, CAG-PA-05; design §4; tasks 5.9.
/// </summary>
public sealed class EfProfileAnalysisRepositoryFreshnessTests
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();

    // -----------------------------------------------------------------------
    // GetLatestAnalyzedCompletedAtAsync — returns MAX CompletedAtUtc of Analyzed rows
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetLatestAnalyzedCompletedAtAsync_WithSingleAnalyzedRow_ReturnsCompletedAt()
    {
        var completedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await using var db = CreateDb();
        Seed(db, _orgId, _clientId, "Analyzed", completedAt);
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.GetLatestAnalyzedCompletedAtAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Equal(completedAt, result);
    }

    [Fact]
    public async Task GetLatestAnalyzedCompletedAtAsync_WithTwoAnalyzedRows_ReturnsMaxCompletedAt()
    {
        var older = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await using var db = CreateDb();
        Seed(db, _orgId, _clientId, "Analyzed", older);
        Seed(db, _orgId, _clientId, "Analyzed", newer);
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.GetLatestAnalyzedCompletedAtAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Equal(newer, result);
    }

    [Fact]
    public async Task GetLatestAnalyzedCompletedAtAsync_WithNoAnalyzedRows_ReturnsNull()
    {
        await using var db = CreateDb();
        // Only a Failed row — no Analyzed
        Seed(db, _orgId, _clientId, "Failed", DateTime.UtcNow.AddDays(-1));
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.GetLatestAnalyzedCompletedAtAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // HasNonTerminalAnalysisForClientAsync — true when Requested/Scraping/Analyzing row exists
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HasNonTerminalAnalysisForClientAsync_WithRequestedRow_ReturnsTrue()
    {
        await using var db = CreateDb();
        Seed(db, _orgId, _clientId, "Requested", null);
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.HasNonTerminalAnalysisForClientAsync(_orgId, _clientId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HasNonTerminalAnalysisForClientAsync_WithScrapingRow_ReturnsTrue()
    {
        await using var db = CreateDb();
        Seed(db, _orgId, _clientId, "Scraping", null);
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.HasNonTerminalAnalysisForClientAsync(_orgId, _clientId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HasNonTerminalAnalysisForClientAsync_WithOnlyAnalyzedOrFailedRows_ReturnsFalse()
    {
        var completedAt = DateTime.UtcNow.AddDays(-1);
        await using var db = CreateDb();
        Seed(db, _orgId, _clientId, "Analyzed", completedAt);
        Seed(db, _orgId, _clientId, "Failed", completedAt.AddDays(-2));
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.HasNonTerminalAnalysisForClientAsync(_orgId, _clientId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HasNonTerminalAnalysisForClientAsync_WithNoRows_ReturnsFalse()
    {
        await using var db = CreateDb();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.HasNonTerminalAnalysisForClientAsync(_orgId, _clientId, CancellationToken.None);

        Assert.False(result);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static AppIdentityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"profile-analysis-freshness-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }

    private static void Seed(
        AppIdentityDbContext db,
        Guid orgId,
        Guid clientId,
        string status,
        DateTime? completedAt)
    {
        db.ProfileAnalyses.Add(new ProfileAnalysisData
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            ClientId = clientId,
            ProfileUrlOrHandle = "@handle",
            Status = status,
            CompletedAtUtc = completedAt,
            CreatedAtUtc = completedAt?.AddHours(-2) ?? DateTime.UtcNow.AddHours(-3),
            UpdatedAtUtc = completedAt ?? DateTime.UtcNow.AddHours(-2),
        });
    }
}
