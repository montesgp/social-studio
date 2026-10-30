using System.Text.Json;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Batches;
using SocialStudio.Domain.Social.SingleAnalyzer;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Batches;

/// <summary>
/// Turning chosen proposals into real background work.
///
/// Each selected proposal becomes its own synthesized analysis, because the generation pipeline reads
/// context from an analysis and fails a job without one. One job per (proposal x format) keeps the
/// existing (JobId, FormatKey) result contract intact.
/// </summary>
public sealed class ProposalGenerationStarterTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();

    [Fact]
    public async Task Start_CreatesOneJobPerProposalAndFormat()
    {
        var (starter, analyses, jobs, batches, queue) = Build();

        var started = await starter.StartAsync(
            new ProposalGenerationRequest(Org, Client,
                [Proposal("Tema uno"), Proposal("Tema dos")],
                ["reel-script", "feed-carousel"]),
            CancellationToken.None);

        Assert.Equal(4, started.TotalPieces);
        Assert.Equal(4, jobs.Inserted.Count);
        Assert.Equal(4, queue.Enqueued.Count);
        Assert.Single(batches.Inserted);

        // One analysis per PROPOSAL, not per piece: both formats of a topic share its context.
        Assert.Equal(2, analyses.Added.Count);
    }

    [Fact]
    public async Task Start_PersistsAnAnalysisThePipelineCanRead()
    {
        var (starter, analyses, _, _, _) = Build();

        await starter.StartAsync(
            new ProposalGenerationRequest(Org, Client, [Proposal("Cómo migrar a un CRM")], ["reel-script"]),
            CancellationToken.None);

        var record = Assert.Single(analyses.Added);

        // The processor loads the analysis and fails the job when AnalysisJson is missing or unusable.
        Assert.Equal(SingleContentAnalysisStatus.Analyzed, record.Status);
        Assert.False(string.IsNullOrWhiteSpace(record.AnalysisJson));

        var analysis = JsonSerializer.Deserialize<SingleAnalyzerAnalysis>(
            record.AnalysisJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(analysis);
        Assert.Contains("Cómo migrar a un CRM", analysis!.Summary);
    }

    [Fact]
    public async Task Start_PointsEachJobAtItsOwnProposalAnalysis()
    {
        var (starter, analyses, jobs, _, _) = Build();

        await starter.StartAsync(
            new ProposalGenerationRequest(Org, Client,
                [Proposal("Tema uno"), Proposal("Tema dos")],
                ["reel-script"]),
            CancellationToken.None);

        var analysisIds = analyses.Added.Select(a => a.Id).ToHashSet();

        Assert.Equal(2, jobs.Inserted.Select(j => j.AnalysisId).Distinct().Count());
        Assert.All(jobs.Inserted, job => Assert.Contains(job.AnalysisId, analysisIds));
    }

    [Fact]
    public async Task Start_GivesEachJobExactlyOneFormat()
    {
        var (starter, _, jobs, _, _) = Build();

        await starter.StartAsync(
            new ProposalGenerationRequest(Org, Client, [Proposal("Tema")], ["reel-script", "feed-carousel"]),
            CancellationToken.None);

        // A format result row is keyed (JobId, FormatKey); two formats in one job would overwrite.
        Assert.All(jobs.Inserted, job => Assert.Single(job.RequestedFormats));
    }

    [Fact]
    public async Task Start_TiesEveryJobToTheBatch()
    {
        var (starter, _, jobs, batches, _) = Build();

        await starter.StartAsync(
            new ProposalGenerationRequest(Org, Client, [Proposal("Tema")], ["reel-script", "feed-carousel"]),
            CancellationToken.None);

        var batchId = batches.Inserted[0].Id;
        Assert.All(jobs.Inserted, job => Assert.Equal(batchId, job.BatchId));
        Assert.Equal([0, 1], jobs.Inserted.Select(j => j.BatchPieceIndex).OrderBy(i => i).ToArray());
    }

    [Fact]
    public async Task Start_KeepsTheProposalReferenceInTheContext()
    {
        var (starter, analyses, _, _, _) = Build();

        await starter.StartAsync(
            new ProposalGenerationRequest(Org, Client,
                [new SelectedProposal("Tema", "resumen", "@referente")],
                ["reel-script"]),
            CancellationToken.None);

        // Provenance must survive: months later the bank has to show which reference inspired a piece.
        Assert.Contains("@referente", analyses.Added[0].AnalysisJson);
    }

    [Fact]
    public async Task Start_EnqueuesOnlyAfterEveryJobIsPersisted()
    {
        var (starter, _, jobs, _, queue) = Build();

        await starter.StartAsync(
            new ProposalGenerationRequest(Org, Client, [Proposal("a"), Proposal("b")], ["reel-script"]),
            CancellationToken.None);

        // A worker that starts on piece 0 mid-write would read a batch whose total is still growing.
        Assert.Equal(
            jobs.Inserted.Select(j => j.Id).OrderBy(id => id),
            queue.Enqueued.OrderBy(id => id));
    }

    [Fact]
    public async Task Start_RejectsAnEmptySelection()
    {
        var (starter, analyses, jobs, batches, queue) = Build();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            starter.StartAsync(
                new ProposalGenerationRequest(Org, Client, [], ["reel-script"]),
                CancellationToken.None));

        Assert.Empty(analyses.Added);
        Assert.Empty(jobs.Inserted);
        Assert.Empty(batches.Inserted);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task Start_RejectsAnEmptyFormatList()
    {
        var (starter, _, jobs, _, _) = Build();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            starter.StartAsync(
                new ProposalGenerationRequest(Org, Client, [Proposal("Tema")], []),
                CancellationToken.None));

        Assert.Empty(jobs.Inserted);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static SelectedProposal Proposal(string title) => new(title, null, null);

    private static (
        ProposalGenerationStarter Starter,
        FakeAnalyses Analyses,
        FakeJobs Jobs,
        FakeBatches Batches,
        FakeQueue Queue) Build()
    {
        var analyses = new FakeAnalyses();
        var jobs = new FakeJobs();
        var batches = new FakeBatches();
        var queue = new FakeQueue();

        return (new ProposalGenerationStarter(analyses, jobs, batches, queue), analyses, jobs, batches, queue);
    }

    private sealed class FakeAnalyses : ISingleContentAnalysisRepository
    {
        public List<SingleContentAnalysisRecord> Added { get; } = [];

        public Task AddAsync(SingleContentAnalysisRecord analysis, CancellationToken ct = default)
        {
            Added.Add(analysis);
            return Task.CompletedTask;
        }

        public Task<SingleContentAnalysisRecord?> GetByIdAsync(Guid analysisId, Guid organizationId, CancellationToken ct = default) =>
            Task.FromResult(Added.FirstOrDefault(a => a.Id == analysisId));

        public Task UpdateStatusAsync(
            Guid analysisId, Guid organizationId, SingleContentAnalysisStatus status, DateTime updatedAtUtc,
            CancellationToken ct = default, string? failureCode = null, string? failureMessageKey = null) =>
            Task.CompletedTask;

        public Task UpdateAsync(SingleContentAnalysisRecord analysis, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(
            Guid organizationId, Guid userId, int page, int pageSize, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(
            Guid organizationId, Guid userId, string normalizedUrl, string sourceUrl, CancellationToken ct = default) =>
            Task.FromResult<SingleContentAnalysisHistoryItem?>(null);

        public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(
            Guid analysisId, Guid organizationId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>?>(null);
    }

    private sealed class FakeJobs : IContentGenerationJobRepository
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

    private sealed class FakeBatches : IContentGenerationBatchRepository
    {
        public List<ContentGenerationBatch> Inserted { get; } = [];

        public Task InsertAsync(ContentGenerationBatch batch, CancellationToken ct)
        {
            Inserted.Add(batch);
            return Task.CompletedTask;
        }

        public Task<ContentGenerationBatch?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult<ContentGenerationBatch?>(null);

        public Task<ContentGenerationBatchProgress?> GetProgressAsync(Guid id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult<ContentGenerationBatchProgress?>(null);

        public Task<IReadOnlyList<ContentGenerationBatch>> ListActiveAsync(Guid organizationId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ContentGenerationBatch>>([]);

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
