using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using SocialStudio.Infrastructure.Persistence.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence;

/// <summary>
/// Gap 1 (incident job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1, Change 5): before this fix
/// <see cref="EfContentGenerationJobRepository.UpdateStatusAsync"/> never touched CompletedAt — only
/// the success path's UpdateManifestAsync set it — so a job that died via cancellation, timeout, or
/// any early "Failed" write kept CompletedAt null forever, making TotalDurationSeconds uncomputable
/// for exactly the jobs an operator most needs to audit.
/// </summary>
public sealed class EfContentGenerationJobRepositoryUpdateStatusTests
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _analysisId = Guid.NewGuid();
    private readonly Guid _jobId = Guid.NewGuid();

    [Theory]
    [InlineData("Failed")]
    [InlineData("Partial")]
    [InlineData("Completed")]
    public async Task UpdateStatusAsync_TerminalStatus_SetsCompletedAt(string terminalStatus)
    {
        await using var db = CreateDb();
        Seed(db, "Running");
        await db.SaveChangesAsync();

        var repo = new EfContentGenerationJobRepository(db);
        await repo.UpdateStatusAsync(_jobId, terminalStatus, "some-reason", "some-code", CancellationToken.None);

        var entity = await db.ContentGenerationJobs.AsNoTracking().SingleAsync(j => j.Id == _jobId);
        Assert.NotNull(entity.CompletedAt);
    }

    [Theory]
    [InlineData("Queued")]
    [InlineData("Running")]
    [InlineData("AnalyzingProfile")]
    public async Task UpdateStatusAsync_NonTerminalStatus_LeavesCompletedAtNull(string nonTerminalStatus)
    {
        await using var db = CreateDb();
        Seed(db, "Queued");
        await db.SaveChangesAsync();

        var repo = new EfContentGenerationJobRepository(db);
        await repo.UpdateStatusAsync(_jobId, nonTerminalStatus, null, null, CancellationToken.None);

        var entity = await db.ContentGenerationJobs.AsNoTracking().SingleAsync(j => j.Id == _jobId);
        Assert.Null(entity.CompletedAt);
    }

    [Fact]
    public async Task UpdateStatusAsync_TerminalStatus_DoesNotOverwriteAnAlreadySetCompletedAt()
    {
        // The success path calls UpdateManifestAsync (which sets CompletedAt) BEFORE
        // UpdateStatusAsync("Completed", ...) — the guard must preserve that earlier timestamp
        // rather than nudging it forward by however long the status write takes.
        await using var db = CreateDb();
        Seed(db, "Running");
        await db.SaveChangesAsync();

        var repo = new EfContentGenerationJobRepository(db);
        var manifestCompletedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        await repo.UpdateManifestAsync(_jobId, "{}", manifestCompletedAt, CancellationToken.None);

        await repo.UpdateStatusAsync(_jobId, "Completed", null, null, CancellationToken.None);

        var entity = await db.ContentGenerationJobs.AsNoTracking().SingleAsync(j => j.Id == _jobId);
        Assert.Equal(manifestCompletedAt, entity.CompletedAt);
    }

    [Fact]
    public async Task Requeue_StartsTheNewRunsClockOver_SoTheWatchdogDoesNotReapIt()
    {
        // Regenerating a format re-queues the SAME job row hours after its first run. The new run
        // must be timed from its own start, not the first run's, or the watchdog fails it at once.
        await using var db = CreateDb();
        Seed(db, "Completed");
        await db.SaveChangesAsync();
        var firstRun = await db.ContentGenerationJobs.SingleAsync(j => j.Id == _jobId);
        firstRun.StartedAt = DateTime.UtcNow.AddHours(-6);
        firstRun.CompletedAt = DateTime.UtcNow.AddHours(-5);
        await db.SaveChangesAsync();

        var repo = new EfContentGenerationJobRepository(db);
        await repo.UpdateStatusAsync(_jobId, "Queued", null, null, CancellationToken.None);
        await repo.UpdateStatusAsync(_jobId, "Running", null, null, CancellationToken.None);

        var entity = await db.ContentGenerationJobs.AsNoTracking().SingleAsync(j => j.Id == _jobId);
        Assert.True(DateTime.UtcNow - entity.StartedAt!.Value < TimeSpan.FromMinutes(1));
        Assert.Null(entity.CompletedAt);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AppIdentityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"content-generation-job-update-status-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }

    private void Seed(AppIdentityDbContext db, string status)
    {
        db.ContentGenerationJobs.Add(new ContentGenerationJobData
        {
            Id = _jobId,
            OrganizationId = _orgId,
            ClientId = _clientId,
            AnalysisId = _analysisId,
            RequestedFormats = ["story-carousel"],
            Status = status,
            BrandSnapshotJson = "{}",
            CorrelationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            CompletedAt = null,
        });
    }
}
