using System.Runtime.CompilerServices;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Regenerate;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Regenerate;

/// <summary>
/// Regeneration is now ASYNC and re-runs the real pipeline: it clears the target format's cached AI
/// payload, sets the objection on the job, and re-enqueues the job (other formats reuse their cache).
/// </summary>
public sealed class RegenerateFormatAssetCommandHandlerTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOrgId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ClientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnalysisId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid JobId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid StoryResultId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid HookResultId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private static ContentGenerationJob MakeJob() =>
        new(
            Id: JobId,
            OrganizationId: OrgId,
            ClientId: ClientId,
            AnalysisId: AnalysisId,
            RequestedFormats: ["story", "hook"],
            Status: "Completed",
            StatusReason: null,
            ErrorCode: null,
            BrandSnapshotJson: "{}",
            ManifestJson: null,
            CorrelationId: Guid.NewGuid(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            StartedAt: null,
            CompletedAt: DateTime.UtcNow);

    private static List<ContentGenerationFormatResult> MakeResults() =>
    [
        new(Id: StoryResultId, JobId: JobId, FormatKey: "story", Status: "Rendered",
            OutputJson: """{"title":"Old Title"}""", CompositionSpecJson: "{}",
            RenderedAssetUri: "asset-hash://x", InferredFieldsJson: null, PromptVersion: "v1",
            ErrorCode: null, CreatedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow),
        new(Id: HookResultId, JobId: JobId, FormatKey: "hook", Status: "Rendered",
            OutputJson: """{"text":"hook text"}""", CompositionSpecJson: null,
            RenderedAssetUri: null, InferredFieldsJson: null, PromptVersion: "v1",
            ErrorCode: null, CreatedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow),
    ];

    [Fact]
    public async Task ClearsTargetFormatPayload_SetsObjection_AndReEnqueues()
    {
        var results = new FakeFormatResultRepo(MakeResults());
        var jobs = new FakeJobRepo(MakeJob());
        var queue = new FakeQueue();
        var handler = new RegenerateFormatAssetCommandHandler(
            results, jobs, new FakeAnalysisRepo(OrgId, AnalysisId), queue);

        var result = await handler.Handle(
            new RegenerateFormatAssetCommand(AnalysisId, OrgId, ClientId, "story", "no CTA on slides"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : "");
        Assert.Equal(JobId, result.Value.JobId);
        Assert.Equal("Queued", result.Value.Status);

        // Target format payload cleared so the processor re-generates it.
        Assert.NotNull(results.LastUpsert);
        Assert.Equal("story", results.LastUpsert!.FormatKey);
        Assert.Null(results.LastUpsert.OutputJson);

        // Objection set on the job + job re-enqueued.
        Assert.Equal("no CTA on slides", jobs.LastObjection);
        Assert.Contains(JobId, queue.Enqueued);
        Assert.Contains("Queued", jobs.StatusUpdates);
    }

    [Fact]
    public async Task UnknownFormat_ReturnsNotFound()
    {
        var handler = new RegenerateFormatAssetCommandHandler(
            new FakeFormatResultRepo(MakeResults()), new FakeJobRepo(MakeJob()),
            new FakeAnalysisRepo(OrgId, AnalysisId), new FakeQueue());

        var result = await handler.Handle(
            new RegenerateFormatAssetCommand(AnalysisId, OrgId, ClientId, "carousel", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("content-gen.format-not-found", result.Error.Code);
    }

    [Fact]
    public async Task CrossTenant_ReturnsAnalysisNotFound()
    {
        var handler = new RegenerateFormatAssetCommandHandler(
            new FakeFormatResultRepo(MakeResults()), new FakeJobRepo(MakeJob()),
            new FakeAnalysisRepo(OrgId, AnalysisId), new FakeQueue());

        var result = await handler.Handle(
            new RegenerateFormatAssetCommand(AnalysisId, OtherOrgId, ClientId, "story", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task NoJob_ReturnsJobNotFound()
    {
        var handler = new RegenerateFormatAssetCommandHandler(
            new FakeFormatResultRepo(MakeResults()), new FakeJobRepo(null),
            new FakeAnalysisRepo(OrgId, AnalysisId), new FakeQueue());

        var result = await handler.Handle(
            new RegenerateFormatAssetCommand(AnalysisId, OrgId, ClientId, "story", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}

file sealed class FakeFormatResultRepo(List<ContentGenerationFormatResult> results)
    : IContentGenerationFormatResultRepository
{
    public ContentGenerationFormatResult? LastUpsert { get; private set; }

    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct)
    {
        LastUpsert = result;
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>(results);
}

file sealed class FakeJobRepo(ContentGenerationJob? job) : IContentGenerationJobRepository
{
    public string? LastObjection { get; private set; }
    public List<string> StatusUpdates { get; } = [];

    public Task InsertAsync(ContentGenerationJob job, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct) =>
        Task.FromResult(job?.OrganizationId == organizationId ? job : null);
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult(job);
    public Task UpdateStatusAsync(Guid id, string status, string? statusReason, string? errorCode, CancellationToken ct)
    {
        StatusUpdates.Add(status);
        return Task.CompletedTask;
    }
    public Task UpdateManifestAsync(Guid id, string manifestJson, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(
        Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) =>
        Task.FromResult(
            job is not null && job.AnalysisId == analysisId && job.OrganizationId == organizationId && job.ClientId == clientId
                ? job : null);
    public Task SetRegenerationObjectionAsync(Guid id, string? objection, CancellationToken ct)
    {
        LastObjection = objection;
        return Task.CompletedTask;
    }
}

file sealed class FakeQueue : IContentGenerationJobQueue
{
    public List<Guid> Enqueued { get; } = [];
    public Task EnqueueAsync(Guid jobId, CancellationToken ct)
    {
        Enqueued.Add(jobId);
        return Task.CompletedTask;
    }
#pragma warning disable CS1998
    public async IAsyncEnumerable<Guid> DequeueAllAsync([EnumeratorCancellation] CancellationToken ct)
    {
        yield break;
    }
#pragma warning restore CS1998
}

file sealed class FakeAnalysisRepo(Guid ownerOrgId, Guid analysisId) : ISingleContentAnalysisRepository
{
    public Task AddAsync(SingleContentAnalysisRecord analysis, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<SingleContentAnalysisRecord?> GetByIdAsync(Guid aId, Guid orgId, CancellationToken cancellationToken = default) =>
        Task.FromResult(aId == analysisId && orgId == ownerOrgId
            ? new SingleContentAnalysisRecord(
                Id: analysisId, OrganizationId: ownerOrgId, ClientId: null, UserId: Guid.NewGuid(),
                SourceUrl: "https://example.com", NormalizedUrl: null, SourcePlatformKey: null,
                Status: SingleContentAnalysisStatus.Analyzed, ContentSnapshotJson: null, AnalysisJson: null,
                ProviderDiagnosticsJson: null, FailureCode: null, FailureMessageKey: null,
                CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow, CompletedAtUtc: DateTime.UtcNow)
            : null);

    public Task UpdateStatusAsync(Guid analysisId, Guid organizationId, SingleContentAnalysisStatus status,
        DateTime updatedAtUtc, CancellationToken cancellationToken = default,
        string? failureCode = null, string? failureMessageKey = null) => Task.CompletedTask;
    public Task UpdateAsync(SingleContentAnalysisRecord analysis, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(Guid organizationId, Guid userId, int page,
        int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SingleContentAnalysisHistoryPage([], 0, page, pageSize));
    public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(Guid organizationId, Guid userId,
        string normalizedUrl, string sourceUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult<SingleContentAnalysisHistoryItem?>(null);
    public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(Guid analysisId, Guid organizationId, Guid userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>?>([]);
}
