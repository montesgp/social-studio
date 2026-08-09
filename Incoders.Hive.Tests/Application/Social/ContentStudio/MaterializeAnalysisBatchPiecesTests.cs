using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Application.Features.Social.ContentStudio.Materialize;
using Incoders.Hive.Domain.Social.ContentStudio;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Application.Social.ContentStudio;

/// <summary>
/// A batch of N pieces of the SAME format must land in the bank as N items.
///
/// The studio used to keep only the latest result per format key, which is right for a regenerated
/// format (the new one supersedes the old) but wrong for a batch: its pieces are siblings, not
/// revisions of each other. Four carousels collapsed into one item — three fully generated, fully
/// paid-for pieces discarded in silence.
/// </summary>
public sealed class MaterializeAnalysisBatchPiecesTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly Guid Analysis = Guid.NewGuid();
    private static readonly Guid Batch = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Materialize_KeepsEveryPieceOfABatchEvenWhenTheFormatRepeats()
    {
        await using var db = CreateDbContext();
        var (handler, items) = Build(db, [
            BatchPiece("feed-carousel", "spec-1", Now),
            BatchPiece("feed-carousel", "spec-2", Now.AddMinutes(1)),
            BatchPiece("feed-carousel", "spec-3", Now.AddMinutes(2)),
            BatchPiece("feed-carousel", "spec-4", Now.AddMinutes(3))
        ]);

        var result = await handler.Handle(new MaterializeAnalysisCommand(Org, Client, Analysis), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var stored = await items.ListBySetAsync(Org, new ContentSetId(result.Value.Id), CancellationToken.None);
        Assert.Equal(4, stored.Count);
        Assert.Equal(4, stored.Select(i => i.Origin.FormatResultId).Distinct().Count());
    }

    [Fact]
    public async Task Materialize_KeepsAMixedBatchIntact()
    {
        await using var db = CreateDbContext();
        var (handler, items) = Build(db, [
            BatchPiece("reel-script", "spec-a", Now),
            BatchPiece("reel-script", "spec-b", Now.AddMinutes(1)),
            BatchPiece("feed-carousel", "spec-c", Now.AddMinutes(2)),
            BatchPiece("feed-carousel", "spec-d", Now.AddMinutes(3)),
            BatchPiece("story-single", "spec-e", Now.AddMinutes(4))
        ]);

        var result = await handler.Handle(new MaterializeAnalysisCommand(Org, Client, Analysis), CancellationToken.None);

        var stored = await items.ListBySetAsync(Org, new ContentSetId(result.Value.Id), CancellationToken.None);
        Assert.Equal(5, stored.Count);
    }

    [Fact]
    public async Task Materialize_IsIdempotentForBatchPieces()
    {
        await using var db = CreateDbContext();
        var (handler, items) = Build(db, [
            BatchPiece("feed-carousel", "spec-1", Now),
            BatchPiece("feed-carousel", "spec-2", Now.AddMinutes(1))
        ]);

        var first = await handler.Handle(new MaterializeAnalysisCommand(Org, Client, Analysis), CancellationToken.None);
        var second = await handler.Handle(new MaterializeAnalysisCommand(Org, Client, Analysis), CancellationToken.None);

        Assert.Equal(first.Value.Id, second.Value.Id);

        // Re-opening the studio must not duplicate the pieces it already holds.
        var stored = await items.ListBySetAsync(Org, new ContentSetId(second.Value.Id), CancellationToken.None);
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task Materialize_AddsPiecesFromASecondBatchOfTheSameFormat()
    {
        await using var db = CreateDbContext();
        var repo = new MutableBatchFormatResultRepo();
        var items = new EfContentItemRepository(db);
        var handler = new MaterializeAnalysisCommandHandler(
            new EfContentSetRepository(db), items, repo, new NoAssetRepo(), new NoPackageRepo(), new StubClock(Now));

        // The SAME result instance must survive into the second read — a piece keeps its identity
        // across re-materializations; only a new piece is new.
        var firstPiece = BatchPiece("feed-carousel", "spec-1", Now);

        repo.Set([firstPiece]);
        var first = await handler.Handle(new MaterializeAnalysisCommand(Org, Client, Analysis), CancellationToken.None);

        // A second batch orders the same format again; those pieces are new, not replacements.
        repo.Set([
            firstPiece,
            BatchPiece("feed-carousel", "spec-2", Now.AddHours(1))
        ]);
        var second = await handler.Handle(new MaterializeAnalysisCommand(Org, Client, Analysis), CancellationToken.None);

        Assert.Equal(first.Value.Id, second.Value.Id);
        var stored = await items.ListBySetAsync(Org, new ContentSetId(second.Value.Id), CancellationToken.None);
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task Materialize_StillSupersedesARegeneratedNonBatchFormat()
    {
        await using var db = CreateDbContext();
        var repo = new MutableBatchFormatResultRepo();
        var items = new EfContentItemRepository(db);
        var handler = new MaterializeAnalysisCommandHandler(
            new EfContentSetRepository(db), items, repo, new NoAssetRepo(), new NoPackageRepo(), new StubClock(Now));

        var jobId = Guid.NewGuid();
        var resultId = Guid.NewGuid();

        // No batch: the original single-format path, where a newer result REPLACES the old one.
        repo.Set([Result(resultId, jobId, "feed-carousel", "spec-v1", Now, batchId: null)]);
        var first = await handler.Handle(new MaterializeAnalysisCommand(Org, Client, Analysis), CancellationToken.None);

        repo.Set([Result(resultId, jobId, "feed-carousel", "spec-v2", Now.AddHours(1), batchId: null)]);
        var second = await handler.Handle(new MaterializeAnalysisCommand(Org, Client, Analysis), CancellationToken.None);

        Assert.Equal(first.Value.Id, second.Value.Id);
        var stored = await items.ListBySetAsync(Org, new ContentSetId(second.Value.Id), CancellationToken.None);
        Assert.Single(stored);
        Assert.Equal("spec-v2", stored[0].Draft.VisualSpecMarkup);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static ContentGenerationFormatResult BatchPiece(string formatKey, string spec, DateTime updatedAt) =>
        // Each piece of a batch is its OWN job, which is exactly what makes the pieces differ.
        Result(Guid.NewGuid(), Guid.NewGuid(), formatKey, spec, updatedAt, Batch);

    private static ContentGenerationFormatResult Result(
        Guid id, Guid jobId, string formatKey, string spec, DateTime updatedAt, Guid? batchId) =>
        new(id, jobId, formatKey, "Succeeded", OutputJson: null, CompositionSpecJson: spec,
            RenderedAssetUri: null, InferredFieldsJson: null, PromptVersion: "v1", ErrorCode: null,
            CreatedAt: Now, UpdatedAt: updatedAt, BatchId: batchId);

    private static (MaterializeAnalysisCommandHandler Handler, EfContentItemRepository Items) Build(
        AppIdentityDbContext db,
        IReadOnlyList<ContentGenerationFormatResult> results)
    {
        var repo = new MutableBatchFormatResultRepo();
        repo.Set(results);

        var items = new EfContentItemRepository(db);
        var handler = new MaterializeAnalysisCommandHandler(
            new EfContentSetRepository(db), items, repo, new NoAssetRepo(), new NoPackageRepo(), new StubClock(Now));

        return (handler, items);
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"materialize-batch-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}

file sealed class StubClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; } = utcNow;
}

file sealed class MutableBatchFormatResultRepo : IContentGenerationFormatResultRepository
{
    private IReadOnlyList<ContentGenerationFormatResult> _results = [];

    public void Set(IReadOnlyList<ContentGenerationFormatResult> results) => _results = results;

    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByAnalysisAndClientAsync(
        Guid organizationId, Guid analysisId, Guid clientId, CancellationToken ct) =>
        Task.FromResult(_results);
}

file sealed class NoAssetRepo : IContentGenerationFormatAssetRepository
{
    public Task UpsertManyForFormatAsync(Guid formatResultId, IReadOnlyList<FormatAssetRow> assets, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<FormatAssetRow>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<FormatAssetRow>> ListByAnalysisClientAsync(Guid organizationId, Guid analysisId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<FormatAssetRow>> ListByAnalysisAndClientAsync(
        Guid organizationId, Guid analysisId, Guid clientId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FormatAssetRow>>([]);

    public Task DeleteByFormatResultIdAsync(Guid formatResultId, CancellationToken ct) =>
        throw new NotSupportedException();
}

file sealed class NoPackageRepo : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => throw new NotSupportedException();

    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) =>
        Task.FromResult<GeneratedPackage?>(null);
}
