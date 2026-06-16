using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Status;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Orchestration;

/// <summary>
/// TDD tests for <see cref="ContentGenerationJobStatusQueryHandler"/>.
/// RED: references types that don't exist yet.
/// </summary>
public sealed class ContentGenerationJobStatusQueryTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();

    // ─── Happy path: returns status and per-format substatus ────────────────

    [Fact]
    public async Task Handle_ExistingJob_ReturnsStatusAndFormatResults()
    {
        var jobId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, "Running");
        var formatResults = new List<ContentGenerationFormatResult>
        {
            MakeFormatResult(jobId, "carousel", "Generated"),
            MakeFormatResult(jobId, "story", "Pending"),
        };

        var jobRepo = new FakeJobRepository(job);
        var formatResultRepo = new FakeFormatResultRepository(formatResults);
        var handler = new ContentGenerationJobStatusQueryHandler(jobRepo, formatResultRepo);

        var query = new ContentGenerationJobStatusQuery(JobId: jobId, OrganizationId: OrgId);
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(jobId, result.Value.JobId);
        Assert.Equal("Running", result.Value.Status);
        Assert.Equal(2, result.Value.PerFormatStatus.Count);
        Assert.Equal("Generated", result.Value.PerFormatStatus["carousel"]);
        Assert.Equal("Pending", result.Value.PerFormatStatus["story"]);
        Assert.Null(result.Value.ManifestJson); // not completed
    }

    // ─── Completed job includes manifest ─────────────────────────────────────

    [Fact]
    public async Task Handle_CompletedJob_ReturnsManifest()
    {
        var jobId = Guid.NewGuid();
        var manifestJson = "{\"formats\":{\"carousel\":{}}}";
        var job = MakeJob(jobId, OrgId, "Completed", manifestJson: manifestJson);

        var jobRepo = new FakeJobRepository(job);
        var formatResultRepo = new FakeFormatResultRepository([]);
        var handler = new ContentGenerationJobStatusQueryHandler(jobRepo, formatResultRepo);

        var query = new ContentGenerationJobStatusQuery(JobId: jobId, OrganizationId: OrgId);
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(manifestJson, result.Value.ManifestJson);
    }

    [Fact]
    public async Task Handle_ExistingJob_ReturnsPerFormatFailureCounts()
    {
        var jobId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, "Failed", clientId: Guid.NewGuid());
        var jobRepo = new FakeJobRepository(job);
        var formatResultRepo = new FakeFormatResultRepository(
        [
            MakeFormatResult(jobId, "story", "RenderFailed"),
        ]);
        var failurePolicy = new FakeFailurePolicy(new Dictionary<string, int>
        {
            ["story"] = 2,
            ["carousel"] = 1,
        });
        var handler = new ContentGenerationJobStatusQueryHandler(jobRepo, formatResultRepo, failurePolicy);

        var query = new ContentGenerationJobStatusQuery(JobId: jobId, OrganizationId: OrgId);
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.PerFormatFailureCounts!["story"]);
        Assert.Equal(1, result.Value.PerFormatFailureCounts!["carousel"]);
    }


    // ─── Cross-tenant: returns NotFound ──────────────────────────────────────

    [Fact]
    public async Task Handle_CrossTenantJobId_ReturnsNotFound()
    {
        var jobId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, "Running"); // belongs to OrgId

        var jobRepo = new FakeJobRepository(job);
        var formatResultRepo = new FakeFormatResultRepository([]);
        var handler = new ContentGenerationJobStatusQueryHandler(jobRepo, formatResultRepo);

        // Query from a different org
        var query = new ContentGenerationJobStatusQuery(JobId: jobId, OrganizationId: OtherOrgId);
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ─── Unknown job ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UnknownJobId_ReturnsNotFound()
    {
        var jobRepo = new FakeJobRepository(null);
        var formatResultRepo = new FakeFormatResultRepository([]);
        var handler = new ContentGenerationJobStatusQueryHandler(jobRepo, formatResultRepo);

        var query = new ContentGenerationJobStatusQuery(JobId: Guid.NewGuid(), OrganizationId: OrgId);
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ContentGenerationJob MakeJob(
        Guid id, Guid orgId, string status,
        string? manifestJson = null,
        Guid? clientId = null) =>
        new(
            Id: id,
            OrganizationId: orgId,
            ClientId: clientId,
            AnalysisId: Guid.NewGuid(),
            RequestedFormats: ["carousel", "story"],
            Status: status,
            StatusReason: null,
            ErrorCode: null,
            BrandSnapshotJson: "{}",
            ManifestJson: manifestJson,
            CorrelationId: Guid.NewGuid(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            StartedAt: null,
            CompletedAt: null);

    private static ContentGenerationFormatResult MakeFormatResult(
        Guid jobId, string formatKey, string status) =>
        new(
            Id: Guid.NewGuid(),
            JobId: jobId,
            FormatKey: formatKey,
            Status: status,
            OutputJson: null,
            CompositionSpecJson: null,
            RenderedAssetUri: null,
            InferredFieldsJson: null,
            PromptVersion: "v1",
            ErrorCode: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow);
}

// ─── Test doubles ─────────────────────────────────────────────────────────────

file sealed class FakeJobRepository : IContentGenerationJobRepository
{
    private readonly ContentGenerationJob? _job;

    public FakeJobRepository(ContentGenerationJob? job) => _job = job;

    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct)
    {
        if (_job is null) return Task.FromResult<ContentGenerationJob?>(null);
        if (_job.Id == id && _job.OrganizationId == organizationId)
            return Task.FromResult<ContentGenerationJob?>(_job);
        return Task.FromResult<ContentGenerationJob?>(null);
    }

    public Task InsertAsync(ContentGenerationJob job, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) =>
        Task.FromResult<ContentGenerationJob?>(null);
    public Task UpdateStatusAsync(Guid id, string status, string? statusReason, string? errorCode, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateManifestAsync(Guid id, string manifestJson, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationJob>>(new List<ContentGenerationJob>());

    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(
        Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) =>
        Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class FakeFormatResultRepository : IContentGenerationFormatResultRepository
{
    private readonly List<ContentGenerationFormatResult> _results;

    public FakeFormatResultRepository(List<ContentGenerationFormatResult> results) => _results = results;

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>(
            _results.Where(r => r.JobId == jobId).ToList());

    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct) =>
        Task.FromResult(result);
}

file sealed class FakeFailurePolicy(
    IReadOnlyDictionary<string, int> counts) : IContentGenerationFailurePolicy
{
    public Task<IReadOnlyDictionary<string, int>> CountFailedAttemptsByFormatAsync(
        Guid organizationId,
        Guid analysisId,
        Guid clientId,
        IReadOnlyCollection<string> formatKeys,
        CancellationToken cancellationToken)
    {
        var result = formatKeys
            .Where(counts.ContainsKey)
            .ToDictionary(format => format, format => counts[format], StringComparer.OrdinalIgnoreCase);

        return Task.FromResult<IReadOnlyDictionary<string, int>>(result);
    }
}
