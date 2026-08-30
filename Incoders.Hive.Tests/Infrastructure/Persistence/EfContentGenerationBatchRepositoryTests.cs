using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

/// <summary>
/// Integration tests (EF InMemory provider, real LINQ translation) for
/// <see cref="EfContentGenerationBatchRepository"/> — the persistence behind a multi-piece batch.
///
/// A batch owns N jobs, one per piece, because the job/format-result chain below cannot hold two
/// pieces of the same format. Progress is therefore NOT a column to keep in sync; it is derived by
/// counting the child jobs, so a crashed worker can never leave a lying progress number behind.
/// </summary>
public sealed class EfContentGenerationBatchRepositoryTests
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _otherOrgId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _analysisId = Guid.NewGuid();

    [Fact]
    public async Task InsertAsync_RoundTripsTheComposition()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);
        var batchId = Guid.NewGuid();

        await repo.InsertAsync(
            new ContentGenerationBatch(
                Id: batchId,
                OrganizationId: _orgId,
                ClientId: _clientId,
                AnalysisId: _analysisId,
                Status: "Queued",
                Composition: [new ContentGenerationBatchEntry("reel-script", 2), new ContentGenerationBatchEntry("feed-carousel", 4)],
                TotalPieces: 6,
                CorrelationId: Guid.NewGuid(),
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: DateTime.UtcNow,
                CompletedAt: null),
            CancellationToken.None);

        var loaded = await repo.GetByIdAsync(batchId, _orgId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(6, loaded!.TotalPieces);
        Assert.Equal(2, loaded.Composition.Count);
        Assert.Equal("reel-script", loaded.Composition[0].FormatKey);
        Assert.Equal(4, loaded.Composition[1].Count);
    }

    [Fact]
    public async Task GetByIdAsync_IsTenantScoped()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);
        var batchId = await SeedBatchAsync(repo);

        var crossTenant = await repo.GetByIdAsync(batchId, _otherOrgId, CancellationToken.None);

        Assert.Null(crossTenant);
    }

    [Fact]
    public async Task GetProgressAsync_CountsChildJobsByStatus()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);
        var batchId = await SeedBatchAsync(repo);

        SeedJob(db, batchId, 0, "Completed", "reel-script");
        SeedJob(db, batchId, 1, "Completed", "reel-script");
        SeedJob(db, batchId, 2, "Running", "feed-carousel");
        SeedJob(db, batchId, 3, "Queued", "feed-carousel");
        SeedJob(db, batchId, 4, "Failed", "feed-carousel");
        await db.SaveChangesAsync();

        var progress = await repo.GetProgressAsync(batchId, _orgId, CancellationToken.None);

        Assert.NotNull(progress);
        Assert.Equal(2, progress!.CompletedPieces);
        Assert.Equal(1, progress.FailedPieces);
        Assert.Equal(1, progress.RunningPieces);
        Assert.Equal(1, progress.QueuedPieces);
    }

    [Fact]
    public async Task GetProgressAsync_ReportsTheFormatCurrentlyRunning()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);
        var batchId = await SeedBatchAsync(repo);

        SeedJob(db, batchId, 0, "Completed", "reel-script");
        SeedJob(db, batchId, 1, "Running", "feed-carousel");
        await db.SaveChangesAsync();

        var progress = await repo.GetProgressAsync(batchId, _orgId, CancellationToken.None);

        // "Generando pieza 2 de 6 — feed-carousel" needs both the index and the format.
        Assert.Equal("feed-carousel", progress!.CurrentFormatKey);
        Assert.Equal(1, progress.CurrentPieceIndex);
    }

    [Fact]
    public async Task GetProgressAsync_IgnoresJobsFromOtherBatches()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);
        var batchId = await SeedBatchAsync(repo);

        SeedJob(db, batchId, 0, "Completed", "reel-script");
        SeedJob(db, Guid.NewGuid(), 0, "Completed", "reel-script");
        // A legacy single-format job carries no batch at all and must never be counted.
        SeedJob(db, null, null, "Completed", "reel-script");
        await db.SaveChangesAsync();

        var progress = await repo.GetProgressAsync(batchId, _orgId, CancellationToken.None);

        Assert.Equal(1, progress!.CompletedPieces);
    }

    [Fact]
    public async Task GetProgressAsync_IsTenantScoped()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);
        var batchId = await SeedBatchAsync(repo);

        var progress = await repo.GetProgressAsync(batchId, _otherOrgId, CancellationToken.None);

        Assert.Null(progress);
    }

    [Fact]
    public async Task ListActiveAsync_ReturnsOnlyUnfinishedBatchesForTheTenant()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);

        var active = await SeedBatchAsync(repo, status: "Running");
        await SeedBatchAsync(repo, status: "Completed");
        await SeedBatchAsync(repo, status: "Running", organizationId: _otherOrgId);

        var result = await repo.ListActiveAsync(_orgId, CancellationToken.None);

        // The global progress bar polls this: it must never surface another tenant's work.
        Assert.Single(result);
        Assert.Equal(active, result[0].Id);
    }

    [Fact]
    public async Task UpdateStatusAsync_StampsCompletionTime()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);
        var batchId = await SeedBatchAsync(repo);
        var completedAt = new DateTime(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

        await repo.UpdateStatusAsync(batchId, "Completed", completedAt, CancellationToken.None);

        var loaded = await repo.GetByIdAsync(batchId, _orgId, CancellationToken.None);
        Assert.Equal("Completed", loaded!.Status);
        Assert.Equal(completedAt, loaded.CompletedAt);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> SeedBatchAsync(
        EfContentGenerationBatchRepository repo,
        string status = "Queued",
        Guid? organizationId = null)
    {
        var batchId = Guid.NewGuid();

        await repo.InsertAsync(
            new ContentGenerationBatch(
                Id: batchId,
                OrganizationId: organizationId ?? _orgId,
                ClientId: _clientId,
                AnalysisId: _analysisId,
                Status: status,
                Composition: [new ContentGenerationBatchEntry("reel-script", 2), new ContentGenerationBatchEntry("feed-carousel", 4)],
                TotalPieces: 6,
                CorrelationId: Guid.NewGuid(),
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: DateTime.UtcNow,
                CompletedAt: null),
            CancellationToken.None);

        return batchId;
    }

    private void SeedJob(
        AppIdentityDbContext db,
        Guid? batchId,
        int? pieceIndex,
        string status,
        string formatKey)
    {
        db.Set<ContentGenerationJobData>().Add(new ContentGenerationJobData
        {
            Id = Guid.NewGuid(),
            OrganizationId = _orgId,
            ClientId = _clientId,
            AnalysisId = _analysisId,
            BatchId = batchId,
            BatchPieceIndex = pieceIndex,
            RequestedFormats = [formatKey],
            Status = status,
            BrandSnapshotJson = "{}",
            CorrelationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    private static AppIdentityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"content-generation-batch-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
