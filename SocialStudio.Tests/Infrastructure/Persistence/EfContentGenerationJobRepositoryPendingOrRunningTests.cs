using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using SocialStudio.Infrastructure.Persistence.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence;

/// <summary>
/// Integration tests (EF InMemory provider, real LINQ-to-Entities WHERE translation) for
/// <see cref="EfContentGenerationJobRepository.ListPendingOrRunningAsync"/> — the data source
/// behind <c>ContentGenerationJobWatchdog</c>'s orphan-recovery and stuck-sweep.
///
/// Reliability-review finding (issue #67 follow-up): the watchdog's in-memory
/// <c>Status == "Running" || Status == "AnalyzingProfile"</c> filter is dead code for
/// AnalyzingProfile in production because this repository's WHERE clause never returned that
/// status in the first place — a job stranded in AnalyzingProfile after a crash would never be
/// seen by either watchdog pass. A mock-only test (<c>FakeJobRepo</c> in
/// <c>ContentGenerationJobWatchdogTests</c>) could not catch this because the fake echoes back
/// whatever list it is constructed with, bypassing the real EF WHERE translation.
/// </summary>
public sealed class EfContentGenerationJobRepositoryPendingOrRunningTests
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _analysisId = Guid.NewGuid();

    [Fact]
    public async Task ListPendingOrRunningAsync_IncludesAnalyzingProfileJob()
    {
        await using var db = CreateDb();
        Seed(db, "AnalyzingProfile");
        await db.SaveChangesAsync();

        var repo = new EfContentGenerationJobRepository(db);
        var result = await repo.ListPendingOrRunningAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("AnalyzingProfile", result[0].Status);
    }

    [Fact]
    public async Task ListPendingOrRunningAsync_IncludesQueuedAndRunningJobs()
    {
        await using var db = CreateDb();
        Seed(db, "Queued");
        Seed(db, "Running");
        await db.SaveChangesAsync();

        var repo = new EfContentGenerationJobRepository(db);
        var result = await repo.ListPendingOrRunningAsync(CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, j => j.Status == "Queued");
        Assert.Contains(result, j => j.Status == "Running");
    }

    [Fact]
    public async Task ListPendingOrRunningAsync_ExcludesTerminalJobs()
    {
        await using var db = CreateDb();
        Seed(db, "Completed");
        Seed(db, "Failed");
        Seed(db, "Partial");
        await db.SaveChangesAsync();

        var repo = new EfContentGenerationJobRepository(db);
        var result = await repo.ListPendingOrRunningAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AppIdentityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"content-generation-job-pending-or-running-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }

    private void Seed(AppIdentityDbContext db, string status)
    {
        db.ContentGenerationJobs.Add(new ContentGenerationJobData
        {
            Id = Guid.NewGuid(),
            OrganizationId = _orgId,
            ClientId = _clientId,
            AnalysisId = _analysisId,
            RequestedFormats = ["story"],
            Status = status,
            BrandSnapshotJson = "{}",
            CorrelationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
    }
}
