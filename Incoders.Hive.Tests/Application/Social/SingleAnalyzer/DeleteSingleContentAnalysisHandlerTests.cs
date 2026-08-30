using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.SingleAnalyzer.History;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Application.Social.SingleAnalyzer;

/// <summary>
/// Covers the hard-delete command. The four guarantees that matter for the UI / DB hygiene:
/// 1) When the repo returns null (not found OR not owned by the caller) → NotFound and we DO NOT
///    save, and we DO NOT touch the storage. No silent partial deletes.
/// 2) When the repo returns a list of storage URIs → save is called once, then each URI is
///    handed to <see cref="IGeneratedPackageStorage.DeleteAsync"/>.
/// 3) When the storage delete throws for one file (file already gone, disk error, etc.) → the
///    handler still returns Success and keeps trying the rest. DB rows are already gone; an
///    orphan ZIP on disk is harmless, but losing the success would be misleading.
/// 4) Save is called BEFORE storage delete so we never delete a file whose DB row didn't go.
/// </summary>
public sealed class DeleteSingleContentAnalysisHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid AnalysisId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsNotFound_AndSkipsCommit_AndSkipsStorage_WhenRepoSaysNotFound()
    {
        var repo = new SpyRepository(returnUris: null);
        var storage = new SpyStorage();
        var uow = new SpyUnitOfWork();
        var handler = new DeleteSingleContentAnalysisHandler(repo, storage, uow, NullLogger<DeleteSingleContentAnalysisHandler>.Instance);

        var result = await handler.Handle(
            new DeleteSingleContentAnalysisCommand(AnalysisId, OrgId, UserId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SingleAnalyzerErrors.NotFound, result.Error);
        Assert.Equal(0, uow.SaveCalls);
        Assert.Empty(storage.DeletedRefs);
    }

    [Fact]
    public async Task Handle_DeletesStorageFilesAfterSave_WhenRepoReturnsUris()
    {
        var repo = new SpyRepository(returnUris: ["pkg-zip-A", "pkg-zip-B"]);
        var storage = new SpyStorage();
        var uow = new SpyUnitOfWork();
        var handler = new DeleteSingleContentAnalysisHandler(repo, storage, uow, NullLogger<DeleteSingleContentAnalysisHandler>.Instance);

        var result = await handler.Handle(
            new DeleteSingleContentAnalysisCommand(AnalysisId, OrgId, UserId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, uow.SaveCalls);
        Assert.Equal(["pkg-zip-A", "pkg-zip-B"], storage.DeletedRefs.Select(r => r.Uri));

        // Ownership filter is enforced at the repo call site — the handler must pass the caller's
        // user id, not some org-wide identity, or another user could delete files we own.
        Assert.Equal(UserId, repo.LastUserId);
        Assert.Equal(OrgId, repo.LastOrganizationId);
    }

    [Fact]
    public async Task Handle_StillSucceeds_WhenStorageDeleteThrowsForOneFile()
    {
        var repo = new SpyRepository(returnUris: ["pkg-zip-A", "pkg-zip-MISSING", "pkg-zip-C"]);
        var storage = new SpyStorage { ThrowOnUri = "pkg-zip-MISSING" };
        var uow = new SpyUnitOfWork();
        var handler = new DeleteSingleContentAnalysisHandler(repo, storage, uow, NullLogger<DeleteSingleContentAnalysisHandler>.Instance);

        var result = await handler.Handle(
            new DeleteSingleContentAnalysisCommand(AnalysisId, OrgId, UserId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // It tried all three even though the middle one threw — best-effort cleanup
        Assert.Equal(["pkg-zip-A", "pkg-zip-MISSING", "pkg-zip-C"], storage.AttemptedRefs.Select(r => r.Uri));
        // Only the two that didn't throw should have made it to the success collection
        Assert.Equal(["pkg-zip-A", "pkg-zip-C"], storage.DeletedRefs.Select(r => r.Uri));
    }

    // ─── Test doubles ────────────────────────────────────────────────────────

    private sealed class SpyRepository : ISingleContentAnalysisRepository
    {
        private readonly IReadOnlyList<string>? _returnUris;
        public Guid LastOrganizationId { get; private set; }
        public Guid LastUserId { get; private set; }

        public SpyRepository(IReadOnlyList<string>? returnUris) => _returnUris = returnUris;

        public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(
            Guid analysisId, Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
        {
            LastOrganizationId = organizationId;
            LastUserId = userId;
            return Task.FromResult(_returnUris);
        }

        public Task AddAsync(SingleContentAnalysisRecord a, CancellationToken ct = default) => Task.CompletedTask;
        public Task<SingleContentAnalysisRecord?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct = default) =>
            Task.FromResult<SingleContentAnalysisRecord?>(null);
        public Task UpdateStatusAsync(Guid id, Guid orgId, SingleContentAnalysisStatus status, DateTime updatedAt,
            CancellationToken ct = default, string? failureCode = null, string? failureKey = null) => Task.CompletedTask;
        public Task UpdateAsync(SingleContentAnalysisRecord a, CancellationToken ct = default) => Task.CompletedTask;
        public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(
            Guid orgId, Guid userId, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new SingleContentAnalysisHistoryPage([], 0, page, pageSize));
        public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(
            Guid orgId, Guid userId, string normalizedUrl, string sourceUrl, CancellationToken ct = default) =>
            Task.FromResult<SingleContentAnalysisHistoryItem?>(null);
    }

    private sealed class SpyStorage : IGeneratedPackageStorage
    {
        public List<StoredPackageRef> AttemptedRefs { get; } = new();
        public List<StoredPackageRef> DeletedRefs { get; } = new();
        public string? ThrowOnUri { get; set; }

        public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct)
        {
            AttemptedRefs.Add(refKey);
            if (refKey.Uri == ThrowOnUri)
            {
                throw new FileNotFoundException("simulated missing file");
            }
            DeletedRefs.Add(refKey);
            return Task.CompletedTask;
        }

        public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    private sealed class SpyUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCalls++;
            return Task.FromResult(0);
        }
    }
}
