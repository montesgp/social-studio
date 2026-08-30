using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Batches;
using Incoders.Hive.Domain.Social.ContentFormats;
using Incoders.Hive.Domain.Social.ContentGeneration.Batches;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Batches;

/// <summary>
/// RED tests for <see cref="CreateContentGenerationBatchCommandHandler"/>.
///
/// The handler's whole job is to turn a cart into N queued jobs — one per piece — and nothing else.
/// It must never touch the generation pipeline itself, which already works and is covered by its own
/// suite.
/// </summary>
public sealed class CreateContentGenerationBatchCommandHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid AnalysisId = Guid.NewGuid();

    private static string ScriptKey => "reel-script";
    private static string CarouselKey => "feed-carousel";

    [Fact]
    public async Task Handle_CreatesOneJobPerPiece()
    {
        var (handler, jobs, batches, queue) = Build();

        var result = await handler.Handle(
            Command([new BatchCompositionItem(ScriptKey, 2), new BatchCompositionItem(CarouselKey, 4)]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, result.Value.TotalPieces);
        Assert.Equal(6, jobs.Inserted.Count);
        Assert.Single(batches.Inserted);
        Assert.Equal(6, queue.Enqueued.Count);
    }

    [Fact]
    public async Task Handle_GivesEachJobExactlyOneFormat()
    {
        var (handler, jobs, _, _) = Build();

        await handler.Handle(
            Command([new BatchCompositionItem(CarouselKey, 3)]),
            CancellationToken.None);

        // One format per job is not cosmetic: the format result row is keyed (JobId, FormatKey), so a
        // job holding two carousels would overwrite one with the other.
        Assert.All(jobs.Inserted, job => Assert.Single(job.RequestedFormats));
        Assert.Equal(3, jobs.Inserted.Count(j => j.RequestedFormats[0] == CarouselKey));
    }

    [Fact]
    public async Task Handle_StampsBatchIdAndPieceIndexOnEveryJob()
    {
        var (handler, jobs, batches, _) = Build();

        await handler.Handle(
            Command([new BatchCompositionItem(ScriptKey, 3)]),
            CancellationToken.None);

        var batchId = batches.Inserted[0].Id;
        Assert.All(jobs.Inserted, job => Assert.Equal(batchId, job.BatchId));
        Assert.Equal([0, 1, 2], jobs.Inserted.Select(j => j.BatchPieceIndex).OrderBy(i => i).ToArray());
    }

    [Fact]
    public async Task Handle_GivesEachPieceItsOwnJobId()
    {
        var (handler, jobs, _, _) = Build();

        await handler.Handle(
            Command([new BatchCompositionItem(CarouselKey, 4)]),
            CancellationToken.None);

        // Distinct ids are what make the pieces DIFFER: the pipeline seeds its variation from the
        // job id, so four carousels sharing one id would come out as four clones.
        Assert.Equal(4, jobs.Inserted.Select(j => j.Id).Distinct().Count());
    }

    [Fact]
    public async Task Handle_PlansTheAssortedPortionAcrossCatalogFormats()
    {
        var (handler, jobs, _, _) = Build();

        var result = await handler.Handle(
            Command([], assortedTotal: 10),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, jobs.Inserted.Count);

        // The resolved mix comes back so the UI can show it BEFORE anything is generated.
        Assert.Equal(10, result.Value.Composition.Sum(entry => entry.Count));
        Assert.True(result.Value.Composition.Count > 1);
    }

    [Fact]
    public async Task Handle_MergesExplicitTilesWithTheAssortedTile()
    {
        var (handler, jobs, _, _) = Build();

        var result = await handler.Handle(
            Command([new BatchCompositionItem(ScriptKey, 2)], assortedTotal: 4),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, jobs.Inserted.Count);
        Assert.Equal(6, result.Value.TotalPieces);
    }

    [Fact]
    public async Task Handle_UsesThePerClientCapWhenTheClientSetsOne()
    {
        var (handler, jobs, _, _) = Build(clientMaxPieces: 3);

        var result = await handler.Handle(
            Command([new BatchCompositionItem(ScriptKey, 4)]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BatchErrors.TooManyPieces(3).Code, result.Error.Code);
        Assert.Empty(jobs.Inserted);
    }

    [Fact]
    public async Task Handle_FallsBackToThePlatformDefaultWhenTheClientSetsNoCap()
    {
        var (handler, jobs, _, _) = Build(clientMaxPieces: null, platformDefault: 5);

        var result = await handler.Handle(
            Command([new BatchCompositionItem(ScriptKey, 6)]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BatchErrors.TooManyPieces(5).Code, result.Error.Code);
        Assert.Empty(jobs.Inserted);
    }

    [Fact]
    public async Task Handle_PersistsNothingWhenTheAnalysisIsUnknown()
    {
        var (handler, jobs, batches, queue) = Build(analysisExists: false);

        var result = await handler.Handle(
            Command([new BatchCompositionItem(ScriptKey, 2)]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(jobs.Inserted);
        Assert.Empty(batches.Inserted);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task Handle_RejectsAnEmptyCart()
    {
        var (handler, _, _, _) = Build();

        var result = await handler.Handle(Command([]), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BatchErrors.EmptyComposition.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_RejectsAFormatOutsideTheCatalog()
    {
        var (handler, _, _, _) = Build();

        var result = await handler.Handle(
            Command([new BatchCompositionItem("carousel", 1)]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BatchErrors.UnknownFormat("carousel").Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_EnqueuesEveryJobItPersisted()
    {
        var (handler, jobs, _, queue) = Build();

        await handler.Handle(
            Command([new BatchCompositionItem(ScriptKey, 2), new BatchCompositionItem(CarouselKey, 2)]),
            CancellationToken.None);

        // A persisted-but-never-enqueued piece is a job that sits Queued forever.
        Assert.Equal(
            jobs.Inserted.Select(j => j.Id).OrderBy(id => id),
            queue.Enqueued.OrderBy(id => id));
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static CreateContentGenerationBatchCommand Command(
        IReadOnlyList<BatchCompositionItem> composition,
        int? assortedTotal = null) =>
        new(OrgId, ClientId, AnalysisId, composition, assortedTotal, CorrelationId: null);

    private static (
        CreateContentGenerationBatchCommandHandler Handler,
        FakeJobRepo Jobs,
        FakeBatchRepo Batches,
        FakeQueue Queue) Build(
        int? clientMaxPieces = null,
        int platformDefault = 20,
        bool analysisExists = true)
    {
        var jobs = new FakeJobRepo();
        var batches = new FakeBatchRepo();
        var queue = new FakeQueue();

        var handler = new CreateContentGenerationBatchCommandHandler(
            new FakeAnalysisLookup(analysisExists),
            new FakeClientCapLookup(clientMaxPieces),
            jobs,
            batches,
            queue,
            new ContentGenerationBatchOptions { DefaultMaxPiecesPerBatch = platformDefault });

        return (handler, jobs, batches, queue);
    }

    private sealed class FakeAnalysisLookup(bool exists) : IBatchAnalysisLookup
    {
        public Task<bool> ExistsAsync(Guid analysisId, Guid organizationId, CancellationToken ct) =>
            Task.FromResult(exists);
    }

    private sealed class FakeClientCapLookup(int? maxPieces) : IBatchPieceCapLookup
    {
        public Task<int?> GetClientPieceCapAsync(Guid clientId, Guid organizationId, CancellationToken ct) =>
            Task.FromResult(maxPieces);
    }

    private sealed class FakeJobRepo : IContentGenerationJobRepository
    {
        public List<ContentGenerationJob> Inserted { get; } = [];

        public Task InsertAsync(ContentGenerationJob job, CancellationToken ct)
        {
            Inserted.Add(job);
            return Task.CompletedTask;
        }

        public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult<ContentGenerationJob?>(null);

        public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ContentGenerationJob?>(null);

        public Task UpdateStatusAsync(Guid id, string status, string? statusReason, string? errorCode, CancellationToken ct) =>
            Task.CompletedTask;

        public Task UpdateManifestAsync(Guid id, string manifestJson, DateTime completedAt, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);

        public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(
            Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) =>
            Task.FromResult<ContentGenerationJob?>(null);
    }

    private sealed class FakeBatchRepo : IContentGenerationBatchRepository
    {
        public List<ContentGenerationBatch> Inserted { get; } = [];

        public Task InsertAsync(ContentGenerationBatch batch, CancellationToken ct)
        {
            Inserted.Add(batch);
            return Task.CompletedTask;
        }

        public Task<ContentGenerationBatch?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult(Inserted.FirstOrDefault(b => b.Id == id));

        public Task<ContentGenerationBatchProgress?> GetProgressAsync(Guid id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult<ContentGenerationBatchProgress?>(null);

        public Task<IReadOnlyList<ContentGenerationBatch>> ListActiveAsync(Guid organizationId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ContentGenerationBatch>>(Inserted);

        public Task UpdateStatusAsync(Guid id, string status, DateTime? completedAt, CancellationToken ct) =>
            Task.CompletedTask;

        public Task ReconcileStatusAsync(Guid batchId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeQueue : IContentGenerationJobQueue
    {
        public List<Guid> Enqueued { get; } = [];

        public Task EnqueueAsync(Guid jobId, CancellationToken ct)
        {
            Enqueued.Add(jobId);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<Guid> DequeueAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
