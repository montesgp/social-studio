using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Orchestration;

/// <summary>
/// TDD tests for <see cref="GenerateContentPackageCommandHandler"/>.
/// RED: references types that don't exist yet.
/// </summary>
public sealed class GenerateContentPackageCommandHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid AnalysisId = Guid.NewGuid();

    // ─── Happy path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidCommand_PersistsJobAndEnqueues_ReturnsJobIdAndStatusUrl()
    {
        var analysis = MakeAnalysisRecord(OrgId);
        var analysisRepo = new FakeAnalysisRepository(analysis);
        var jobRepo = new FakeContentGenerationJobRepository();
        var queue = new FakeContentGenerationJobQueue();

        var handler = new GenerateContentPackageCommandHandler(analysisRepo, jobRepo, queue);

        var command = new GenerateContentPackageCommand(
            OrganizationId: OrgId,
            ClientId: ClientId,
            AnalysisId: AnalysisId,
            RequestedFormats: ["carousel", "flyer"],
            CorrelationId: null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value.JobId);
        Assert.NotNull(result.Value.StatusUrl);
        Assert.Contains(result.Value.JobId.ToString(), result.Value.StatusUrl);

        // Job must have been persisted with status Queued
        Assert.Single(jobRepo.Inserted);
        Assert.Equal("Queued", jobRepo.Inserted[0].Status);
        Assert.Equal(OrgId, jobRepo.Inserted[0].OrganizationId);
        Assert.Equal(ClientId, jobRepo.Inserted[0].ClientId);
        Assert.Equal(AnalysisId, jobRepo.Inserted[0].AnalysisId);
        Assert.Equal(2, jobRepo.Inserted[0].RequestedFormats.Count);

        // Job must have been enqueued
        Assert.Single(queue.Enqueued);
        Assert.Equal(result.Value.JobId, queue.Enqueued[0]);
    }

    [Fact]
    public async Task Handle_ClientIdMissingInCommand_UsesAnalysisClient()
    {
        var analysis = MakeAnalysisRecord(OrgId);
        var analysisRepo = new FakeAnalysisRepository(analysis);
        var jobRepo = new FakeContentGenerationJobRepository();
        var queue = new FakeContentGenerationJobQueue();
        var handler = new GenerateContentPackageCommandHandler(analysisRepo, jobRepo, queue);

        var command = new GenerateContentPackageCommand(
            OrganizationId: OrgId,
            ClientId: null,
            AnalysisId: AnalysisId,
            RequestedFormats: ["carousel"],
            CorrelationId: null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(jobRepo.Inserted);
        Assert.Equal(ClientId, jobRepo.Inserted[0].ClientId);
    }

    [Fact]
    public async Task Handle_WithRegenerationObjection_PersistsNormalizedObjection()
    {
        var analysis = MakeAnalysisRecord(OrgId);
        var analysisRepo = new FakeAnalysisRepository(analysis);
        var jobRepo = new FakeContentGenerationJobRepository();
        var queue = new FakeContentGenerationJobQueue();
        var handler = new GenerateContentPackageCommandHandler(analysisRepo, jobRepo, queue);

        var command = new GenerateContentPackageCommand(
            OrganizationId: OrgId,
            ClientId: ClientId,
            AnalysisId: AnalysisId,
            RequestedFormats: ["carousel"],
            CorrelationId: null,
            RegenerationObjection: "  The CTA is inaccurate for this brand.  ");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(jobRepo.Inserted);
        Assert.Equal("The CTA is inaccurate for this brand.", jobRepo.Inserted[0].RegenerationObjection);
    }

    [Fact]
    public async Task Handle_ObjectionTooLong_ReturnsValidationError()
    {
        var analysis = MakeAnalysisRecord(OrgId);
        var analysisRepo = new FakeAnalysisRepository(analysis);
        var jobRepo = new FakeContentGenerationJobRepository();
        var queue = new FakeContentGenerationJobQueue();
        var handler = new GenerateContentPackageCommandHandler(analysisRepo, jobRepo, queue);

        var command = new GenerateContentPackageCommand(
            OrganizationId: OrgId,
            ClientId: ClientId,
            AnalysisId: AnalysisId,
            RequestedFormats: ["carousel"],
            CorrelationId: null,
            RegenerationObjection: new string('x', 1001));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("content-gen.regeneration-objection-too-long", result.Error.Code);
        Assert.Empty(jobRepo.Inserted);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task Handle_NoClientOnCommandOrAnalysis_ReturnsValidationError()
    {
        var analysis = MakeAnalysisRecordWithoutClient(OrgId);
        var analysisRepo = new FakeAnalysisRepository(analysis);
        var jobRepo = new FakeContentGenerationJobRepository();
        var queue = new FakeContentGenerationJobQueue();
        var handler = new GenerateContentPackageCommandHandler(analysisRepo, jobRepo, queue);

        var command = new GenerateContentPackageCommand(
            OrganizationId: OrgId,
            ClientId: null,
            AnalysisId: AnalysisId,
            RequestedFormats: ["carousel"],
            CorrelationId: null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("content-gen.client-required", result.Error.Code);
        Assert.Empty(jobRepo.Inserted);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task Handle_CommandClientDiffersFromAnalysisClient_ReturnsValidationError()
    {
        var analysis = MakeAnalysisRecord(OrgId);
        var analysisRepo = new FakeAnalysisRepository(analysis);
        var jobRepo = new FakeContentGenerationJobRepository();
        var queue = new FakeContentGenerationJobQueue();
        var handler = new GenerateContentPackageCommandHandler(analysisRepo, jobRepo, queue);

        var command = new GenerateContentPackageCommand(
            OrganizationId: OrgId,
            ClientId: Guid.NewGuid(),
            AnalysisId: AnalysisId,
            RequestedFormats: ["carousel"],
            CorrelationId: null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("content-gen.client-mismatch", result.Error.Code);
        Assert.Empty(jobRepo.Inserted);
        Assert.Empty(queue.Enqueued);
    }

    // ─── Cross-tenant: analysis belongs to a different org ──────────────────

    [Fact]
    public async Task Handle_CrossTenantAnalysis_ReturnsNotFound()
    {
        var analysis = MakeAnalysisRecord(Guid.NewGuid()); // different org
        var analysisRepo = new FakeAnalysisRepository(analysis);
        var jobRepo = new FakeContentGenerationJobRepository();
        var queue = new FakeContentGenerationJobQueue();

        var handler = new GenerateContentPackageCommandHandler(analysisRepo, jobRepo, queue);

        var command = new GenerateContentPackageCommand(
            OrganizationId: OrgId, // different from analysis.OrganizationId
            ClientId: null,
            AnalysisId: AnalysisId,
            RequestedFormats: ["carousel"],
            CorrelationId: null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);

        // Nothing should have been persisted or enqueued
        Assert.Empty(jobRepo.Inserted);
        Assert.Empty(queue.Enqueued);
    }

    // ─── Validation: empty formats list ──────────────────────────────────────

    [Fact]
    public async Task Handle_EmptyFormats_ReturnsValidationError()
    {
        var analysis = MakeAnalysisRecord(OrgId);
        var analysisRepo = new FakeAnalysisRepository(analysis);
        var jobRepo = new FakeContentGenerationJobRepository();
        var queue = new FakeContentGenerationJobQueue();

        var handler = new GenerateContentPackageCommandHandler(analysisRepo, jobRepo, queue);

        var command = new GenerateContentPackageCommand(
            OrganizationId: OrgId,
            ClientId: null,
            AnalysisId: AnalysisId,
            RequestedFormats: [],
            CorrelationId: null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // ─── Validation: unknown format key ──────────────────────────────────────

    [Fact]
    public async Task Handle_UnknownFormatKey_ReturnsValidationError()
    {
        var analysis = MakeAnalysisRecord(OrgId);
        var analysisRepo = new FakeAnalysisRepository(analysis);
        var jobRepo = new FakeContentGenerationJobRepository();
        var queue = new FakeContentGenerationJobQueue();

        var handler = new GenerateContentPackageCommandHandler(analysisRepo, jobRepo, queue);

        var command = new GenerateContentPackageCommand(
            OrganizationId: OrgId,
            ClientId: null,
            AnalysisId: AnalysisId,
            RequestedFormats: ["carousel", "unknown-format-xyz"],
            CorrelationId: null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // ─── Analysis not found at all ───────────────────────────────────────────

    [Fact]
    public async Task Handle_AnalysisNotFound_ReturnsNotFound()
    {
        var analysisRepo = new FakeAnalysisRepository(null); // not found
        var jobRepo = new FakeContentGenerationJobRepository();
        var queue = new FakeContentGenerationJobQueue();

        var handler = new GenerateContentPackageCommandHandler(analysisRepo, jobRepo, queue);

        var command = new GenerateContentPackageCommand(
            OrganizationId: OrgId,
            ClientId: null,
            AnalysisId: Guid.NewGuid(),
            RequestedFormats: ["carousel"],
            CorrelationId: null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static SingleContentAnalysisRecord MakeAnalysisRecord(Guid orgId) =>
        MakeAnalysisRecordCore(orgId, ClientId);

    private static SingleContentAnalysisRecord MakeAnalysisRecordWithoutClient(Guid orgId) =>
        MakeAnalysisRecordCore(orgId, null);

    private static SingleContentAnalysisRecord MakeAnalysisRecordCore(Guid orgId, Guid? clientId) =>
        new(
            Id: AnalysisId,
            OrganizationId: orgId,
            ClientId: clientId,
            UserId: Guid.NewGuid(),
            SourceUrl: "https://tiktok.com/@brand/video/1",
            NormalizedUrl: "https://tiktok.com/@brand/video/1",
            SourcePlatformKey: "tiktok",
            Status: SingleContentAnalysisStatus.Analyzed,
            ContentSnapshotJson: null,
            AnalysisJson: "{}",
            ProviderDiagnosticsJson: null,
            FailureCode: null,
            FailureMessageKey: null,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow,
            CompletedAtUtc: DateTime.UtcNow);
}

// ─── Test doubles ─────────────────────────────────────────────────────────────

file sealed class FakeAnalysisRepository : ISingleContentAnalysisRepository
{
    private readonly SingleContentAnalysisRecord? _record;

    public FakeAnalysisRepository(SingleContentAnalysisRecord? record) => _record = record;

    public Task<SingleContentAnalysisRecord?> GetByIdAsync(Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        if (_record is null) return Task.FromResult<SingleContentAnalysisRecord?>(null);
        // Simulate tenant isolation at repo level (org filter)
        if (_record.Id == analysisId && _record.OrganizationId == organizationId)
            return Task.FromResult<SingleContentAnalysisRecord?>(_record);
        return Task.FromResult<SingleContentAnalysisRecord?>(null);
    }

    public Task AddAsync(SingleContentAnalysisRecord analysis, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UpdateStatusAsync(Guid analysisId, Guid organizationId, SingleContentAnalysisStatus status, DateTime updatedAtUtc, CancellationToken cancellationToken = default, string? failureCode = null, string? failureMessageKey = null) => Task.CompletedTask;
    public Task UpdateAsync(SingleContentAnalysisRecord analysis, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(
        Guid organizationId, Guid userId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SingleContentAnalysisHistoryPage([], 0, page, pageSize));

    public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(
        Guid organizationId, Guid userId, string normalizedUrl, string sourceUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult<SingleContentAnalysisHistoryItem?>(null);

    public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(
        Guid analysisId, Guid organizationId, Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>?>(null);
}

file sealed class FakeContentGenerationJobRepository : IContentGenerationJobRepository
{
    public List<ContentGenerationJob> Inserted { get; } = new();

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
        Task.FromResult<IReadOnlyList<ContentGenerationJob>>(new List<ContentGenerationJob>());
}

file sealed class FakeContentGenerationJobQueue : IContentGenerationJobQueue
{
    public List<Guid> Enqueued { get; } = new();

    public Task EnqueueAsync(Guid jobId, CancellationToken ct)
    {
        Enqueued.Add(jobId);
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not needed in this test.");
}
