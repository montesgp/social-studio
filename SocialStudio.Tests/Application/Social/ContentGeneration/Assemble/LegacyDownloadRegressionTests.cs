using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Download;
using SocialStudio.Domain.Common;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Assemble;

/// <summary>
/// T4.4 — Regression: legacy frozen-ZIP download MUST continue to work for packages
/// that have <c>asset-hash://</c> RenderedAssetUri and no per-asset rows.
/// The <see cref="DownloadGeneratedPackageQueryHandler"/> MUST NOT be broken by INC-4.
/// </summary>
public sealed class LegacyDownloadRegressionTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    // ─── Legacy completed job: streams frozen ZIP ──────────────────────────────

    [Fact]
    public async Task LegacyDownload_CompletedJobWithAssetHashUri_StreamsFrozenZip()
    {
        var jobId = Guid.NewGuid();
        var legacyStorageUri = "asset-hash://sha256:abc123";

        var job = MakeJob(jobId, OrgId, "Completed");
        var package = new GeneratedPackage(
            Id: Guid.NewGuid(),
            JobId: jobId,
            StorageUri: legacyStorageUri,
            SizeBytes: 1024,
            ManifestJson: "{}",
            ExpiresAt: null,
            CreatedAt: DateTime.UtcNow);

        var zipBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // ZIP magic
        var packageStream = new MemoryStream(zipBytes);

        var jobRepo = new StubJobRepo(job);
        var packageRepo = new StubPackageRepo(package);
        var storage = new StubPackageStorage(packageStream);

        var handler = new DownloadGeneratedPackageQueryHandler(jobRepo, packageRepo, storage);
        var query = new DownloadGeneratedPackageQuery(JobId: jobId, OrganizationId: OrgId);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.ContentStream);

        // Verify the stream is the frozen ZIP (check magic bytes)
        var buffer = new byte[4];
        var read = await result.Value.ContentStream.ReadAsync(buffer);
        Assert.Equal(4, read);
        Assert.Equal(zipBytes, buffer);
    }

    // ─── Legacy completed job: partial job also downloadable ──────────────────

    [Fact]
    public async Task LegacyDownload_PartialJob_StreamsFrozenZip()
    {
        var jobId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, "Partial");
        var package = new GeneratedPackage(
            Id: Guid.NewGuid(),
            JobId: jobId,
            StorageUri: "asset-hash://sha256:partial",
            SizeBytes: 512,
            ManifestJson: "{}",
            ExpiresAt: null,
            CreatedAt: DateTime.UtcNow);

        var jobRepo = new StubJobRepo(job);
        var packageRepo = new StubPackageRepo(package);
        var storage = new StubPackageStorage(new MemoryStream([0x50, 0x4B]));

        var handler = new DownloadGeneratedPackageQueryHandler(jobRepo, packageRepo, storage);
        var result = await handler.Handle(
            new DownloadGeneratedPackageQuery(jobId, OrgId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // ─── Cross-tenant for legacy job → NotFound (tenant isolation preserved) ──

    [Fact]
    public async Task LegacyDownload_CrossTenant_ReturnsNotFound()
    {
        var jobId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, "Completed");

        var jobRepo = new StubJobRepo(job);
        var packageRepo = new StubPackageRepo(null);
        var storage = new StubPackageStorage(Stream.Null);

        var handler = new DownloadGeneratedPackageQueryHandler(jobRepo, packageRepo, storage);
        var result = await handler.Handle(
            new DownloadGeneratedPackageQuery(jobId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

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
            ManifestJson: "{}",
            CorrelationId: Guid.NewGuid(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            StartedAt: null,
            CompletedAt: DateTime.UtcNow);
}

// ─── Stubs ───────────────────────────────────────────────────────────────────

file sealed class StubJobRepo(ContentGenerationJob? job) : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;

    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) =>
        Task.FromResult(job?.Id == id && job.OrganizationId == orgId ? job : null);

    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) =>
        Task.FromResult<ContentGenerationJob?>(null);

    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? code, CancellationToken ct) =>
        Task.CompletedTask;

    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);

    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(
        Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) =>
        Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class StubPackageRepo(GeneratedPackage? package) : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => Task.CompletedTask;

    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) =>
        Task.FromResult(package?.JobId == jobId ? package : null);
}

file sealed class StubPackageStorage(Stream stream) : IGeneratedPackageStorage
{
    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
        Task.FromResult(new StoredPackageRef("fake://uri"));

    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
        Task.FromResult(stream);

    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) => Task.CompletedTask;
}
