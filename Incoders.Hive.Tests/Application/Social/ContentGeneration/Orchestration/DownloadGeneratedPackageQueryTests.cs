using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Download;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Orchestration;

/// <summary>
/// TDD tests for <see cref="DownloadGeneratedPackageQueryHandler"/>.
/// RED: references types that don't exist yet.
/// </summary>
public sealed class DownloadGeneratedPackageQueryTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    // ─── Happy path: completed job returns storage ref ────────────────────

    [Fact]
    public async Task Handle_CompletedJob_ReturnsStorageRefAndStream()
    {
        var jobId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, "Completed");
        var package = MakePackage(jobId, "orgs/job/package.zip");
        var packageStream = new MemoryStream([0x50, 0x4B, 0x03, 0x04]); // ZIP magic bytes

        var jobRepo = new FakeJobRepo(job);
        var packageRepo = new FakePackageRepo(package);
        var storage = new FakePackageStorage(packageStream);
        var handler = new DownloadGeneratedPackageQueryHandler(jobRepo, packageRepo, storage);

        var query = new DownloadGeneratedPackageQuery(JobId: jobId, OrganizationId: OrgId, UserId: UserId);
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.StorageRef);
        Assert.NotNull(result.Value.ContentStream);
        Assert.Equal(1, packageRepo.MarkDownloadedCalls);
        Assert.Equal(UserId, packageRepo.LastDownloadedByUserId);
    }

    // ─── Job status not completed → NotFound ────────────────────────────────

    [Fact]
    public async Task Handle_PendingJob_ReturnsNotFound()
    {
        var jobId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, "Queued");

        var jobRepo = new FakeJobRepo(job);
        var packageRepo = new FakePackageRepo(null);
        var storage = new FakePackageStorage(Stream.Null);
        var handler = new DownloadGeneratedPackageQueryHandler(jobRepo, packageRepo, storage);

        var query = new DownloadGeneratedPackageQuery(JobId: jobId, OrganizationId: OrgId);
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ─── Cross-tenant returns NotFound ────────────────────────────────────────

    [Fact]
    public async Task Handle_CrossTenantJob_ReturnsNotFound()
    {
        var jobId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, "Completed"); // belongs to OrgId

        var jobRepo = new FakeJobRepo(job);
        var packageRepo = new FakePackageRepo(null);
        var storage = new FakePackageStorage(Stream.Null);
        var handler = new DownloadGeneratedPackageQueryHandler(jobRepo, packageRepo, storage);

        var query = new DownloadGeneratedPackageQuery(JobId: jobId, OrganizationId: OtherOrgId);
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ─── Package record not found (race) → NotFound ───────────────────────────

    [Fact]
    public async Task Handle_JobCompletedButNoPackageRow_ReturnsNotFound()
    {
        var jobId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, "Completed");

        var jobRepo = new FakeJobRepo(job);
        var packageRepo = new FakePackageRepo(null); // no package row yet
        var storage = new FakePackageStorage(Stream.Null);
        var handler = new DownloadGeneratedPackageQueryHandler(jobRepo, packageRepo, storage);

        var query = new DownloadGeneratedPackageQuery(JobId: jobId, OrganizationId: OrgId);
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ContentGenerationJob MakeJob(Guid id, Guid orgId, string status) =>
        new(
            Id: id,
            OrganizationId: orgId,
            ClientId: null,
            AnalysisId: Guid.NewGuid(),
            RequestedFormats: ["carousel"],
            Status: status,
            StatusReason: null,
            ErrorCode: null,
            BrandSnapshotJson: "{}",
            ManifestJson: status == "Completed" ? "{}" : null,
            CorrelationId: Guid.NewGuid(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            StartedAt: null,
            CompletedAt: status == "Completed" ? DateTime.UtcNow : null);

    private static GeneratedPackage MakePackage(Guid jobId, string storageUri) =>
        new(
            Id: Guid.NewGuid(),
            JobId: jobId,
            StorageUri: storageUri,
            SizeBytes: 1024,
            ManifestJson: "{}",
            ExpiresAt: null,
            CreatedAt: DateTime.UtcNow);
}

// ─── Test doubles ─────────────────────────────────────────────────────────────

file sealed class FakeJobRepo : IContentGenerationJobRepository
{
    private readonly ContentGenerationJob? _job;
    public FakeJobRepo(ContentGenerationJob? job) => _job = job;

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

file sealed class FakePackageRepo : IGeneratedPackageRepository
{
    private readonly GeneratedPackage? _package;
    public FakePackageRepo(GeneratedPackage? package) => _package = package;
    public int MarkDownloadedCalls { get; private set; }
    public Guid? LastDownloadedByUserId { get; private set; }

    public Task InsertAsync(GeneratedPackage package, CancellationToken ct) => Task.CompletedTask;
    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) =>
        Task.FromResult(_package?.JobId == jobId ? _package : null);

    public Task MarkDownloadedAsync(Guid jobId, Guid organizationId, Guid? userId, DateTime downloadedAtUtc, CancellationToken ct)
    {
        MarkDownloadedCalls++;
        LastDownloadedByUserId = userId;
        return Task.CompletedTask;
    }
}

file sealed class FakePackageStorage : IGeneratedPackageStorage
{
    private readonly Stream _stream;
    public FakePackageStorage(Stream stream) => _stream = stream;

    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
        Task.FromResult(new StoredPackageRef("fake://uri"));

    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
        Task.FromResult(_stream);

    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) => Task.CompletedTask;
}
