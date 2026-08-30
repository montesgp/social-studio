using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

/// <summary>
/// Tests for batch status roll-up.
///
/// Without this, a finished batch sits at "Queued" forever and the global progress bar never lets go
/// of it. Reconciliation is recomputed from the child jobs after every piece rather than incremented,
/// so a worker that dies mid-batch is corrected by the next piece that finishes instead of leaving a
/// permanently wrong number.
/// </summary>
public sealed class EfContentGenerationBatchRepositoryReconcileTests
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _analysisId = Guid.NewGuid();

    [Fact]
    public async Task Reconcile_MarksCompletedWhenEveryPieceSucceeded()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);
        var batchId = await SeedBatchAsync(repo, totalPieces: 2);

        SeedJob(db, batchId, "Completed");
        SeedJob(db, batchId, "Completed");
        await db.SaveChangesAsync();

        await repo.ReconcileStatusAsync(batchId, CancellationToken.None);

        var batch = await repo.GetByIdAsync(batchId, _orgId, CancellationToken.None);
        Assert.Equal("Completed", batch!.Status);
        Assert.NotNull(batch.CompletedAt);
    }

    [Fact]
    public async Task Reconcile_MarksPartialWhenSomePiecesFailed()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);
        var batchId = await SeedBatchAsync(repo, totalPieces: 2);

        SeedJob(db, batchId, "Completed");
        SeedJob(db, batchId, "Failed");
        await db.SaveChangesAsync();

        await repo.ReconcileStatusAsync(batchId, CancellationToken.None);

        // Partial, not Failed: pieces landed in the bank and the operator can still use them.
        var batch = await repo.GetByIdAsync(batchId, _orgId, CancellationToken.None);
        Assert.Equal("Partial", batch!.Status);
        Assert.NotNull(batch.CompletedAt);
    }

    [Fact]
    public async Task Reconcile_MarksFailedWhenEveryPieceFailed()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);
        var batchId = await SeedBatchAsync(repo, totalPieces: 2);

        SeedJob(db, batchId, "Failed");
        SeedJob(db, batchId, "Failed");
        await db.SaveChangesAsync();

        await repo.ReconcileStatusAsync(batchId, CancellationToken.None);

        var batch = await repo.GetByIdAsync(batchId, _orgId, CancellationToken.None);
        Assert.Equal("Failed", batch!.Status);
    }

    [Fact]
    public async Task Reconcile_MarksRunningWhilePiecesRemain()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);
        var batchId = await SeedBatchAsync(repo, totalPieces: 3);

        SeedJob(db, batchId, "Completed");
        SeedJob(db, batchId, "Running");
        SeedJob(db, batchId, "Queued");
        await db.SaveChangesAsync();

        await repo.ReconcileStatusAsync(batchId, CancellationToken.None);

        var batch = await repo.GetByIdAsync(batchId, _orgId, CancellationToken.None);
        Assert.Equal("Running", batch!.Status);
        Assert.Null(batch.CompletedAt);
    }

    [Fact]
    public async Task Reconcile_DoesNotFinishABatchWhosePiecesAreNotAllPersistedYet()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);
        var batchId = await SeedBatchAsync(repo, totalPieces: 5);

        // Only two of the five pieces exist so far. Closing the batch here would strand the rest.
        SeedJob(db, batchId, "Completed");
        SeedJob(db, batchId, "Completed");
        await db.SaveChangesAsync();

        await repo.ReconcileStatusAsync(batchId, CancellationToken.None);

        var batch = await repo.GetByIdAsync(batchId, _orgId, CancellationToken.None);
        Assert.NotEqual("Completed", batch!.Status);
        Assert.Null(batch.CompletedAt);
    }

    [Fact]
    public async Task Reconcile_IsIdempotent()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);
        var batchId = await SeedBatchAsync(repo, totalPieces: 1);

        SeedJob(db, batchId, "Completed");
        await db.SaveChangesAsync();

        await repo.ReconcileStatusAsync(batchId, CancellationToken.None);
        var first = await repo.GetByIdAsync(batchId, _orgId, CancellationToken.None);

        await repo.ReconcileStatusAsync(batchId, CancellationToken.None);
        var second = await repo.GetByIdAsync(batchId, _orgId, CancellationToken.None);

        // Every finishing piece calls this, so the completion timestamp must not keep moving.
        Assert.Equal(first!.CompletedAt, second!.CompletedAt);
    }

    [Fact]
    public async Task Reconcile_IgnoresAnUnknownBatch()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);

        await repo.ReconcileStatusAsync(Guid.NewGuid(), CancellationToken.None);
    }

    [Fact]
    public async Task ListActive_DropsTheBatchOnceReconciled()
    {
        await using var db = CreateDb();
        var repo = new EfContentGenerationBatchRepository(db);
        var batchId = await SeedBatchAsync(repo, totalPieces: 1);

        SeedJob(db, batchId, "Completed");
        await db.SaveChangesAsync();
        await repo.ReconcileStatusAsync(batchId, CancellationToken.None);

        var active = await repo.ListActiveAsync(_orgId, CancellationToken.None);

        Assert.Empty(active);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> SeedBatchAsync(EfContentGenerationBatchRepository repo, int totalPieces)
    {
        var batchId = Guid.NewGuid();

        await repo.InsertAsync(
            new ContentGenerationBatch(
                Id: batchId,
                OrganizationId: _orgId,
                ClientId: Guid.NewGuid(),
                AnalysisId: _analysisId,
                Status: "Queued",
                Composition: [new ContentGenerationBatchEntry("reel-script", totalPieces)],
                TotalPieces: totalPieces,
                CorrelationId: Guid.NewGuid(),
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: DateTime.UtcNow,
                CompletedAt: null),
            CancellationToken.None);

        return batchId;
    }

    private void SeedJob(AppIdentityDbContext db, Guid batchId, string status)
    {
        db.Set<ContentGenerationJobData>().Add(new ContentGenerationJobData
        {
            Id = Guid.NewGuid(),
            OrganizationId = _orgId,
            AnalysisId = _analysisId,
            BatchId = batchId,
            RequestedFormats = ["reel-script"],
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
            .UseInMemoryDatabase($"content-generation-batch-reconcile-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
