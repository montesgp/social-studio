using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.SingleAnalyzer.History;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Application.Social.SingleAnalyzer;

/// <summary>
/// Covers the read-only list handler that powers the "saved analyses" grid below the analyzer.
/// The handler is thin: it delegates to the repo and maps the page items to the response DTO,
/// flattening the optional ContentGenerationSummary into a sibling DTO. The two relevant assertions
/// are: (a) repo is called with the exact user+org filter, (b) the embedded content-generation
/// summary is preserved (and stays null when there is no job) so the UI can decide what to show.
/// </summary>
public sealed class ListSingleContentAnalysesHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Handle_PassesUserAndOrgToRepository_AndReturnsMappedPage()
    {
        var jobId = Guid.NewGuid();
        var repo = new SpyRepository(new SingleContentAnalysisHistoryPage(
            [
                new SingleContentAnalysisHistoryItem(
                    Id: Guid.NewGuid(),
                    ClientId: null,
                    SourceUrl: "https://www.tiktok.com/@brand/video/123",
                    NormalizedUrl: "https://www.tiktok.com/@brand/video/123",
                    SourcePlatformKey: "tiktok",
                    Status: SingleContentAnalysisStatus.Analyzed,
                    FailureCode: null,
                    CreatedAtUtc: new DateTime(2026, 5, 27, 9, 0, 0, DateTimeKind.Utc),
                    UpdatedAtUtc: new DateTime(2026, 5, 27, 9, 5, 0, DateTimeKind.Utc),
                    CompletedAtUtc: new DateTime(2026, 5, 27, 9, 5, 0, DateTimeKind.Utc),
                    LatestContentGeneration: new ContentGenerationSummary(
                        JobId: jobId,
                        Status: "Completed",
                        HasPackage: true,
                        CompletedAt: new DateTime(2026, 5, 27, 9, 6, 0, DateTimeKind.Utc))),
                new SingleContentAnalysisHistoryItem(
                    Id: Guid.NewGuid(),
                    ClientId: null,
                    SourceUrl: "https://www.instagram.com/p/abc",
                    NormalizedUrl: "https://www.instagram.com/p/abc",
                    SourcePlatformKey: "instagram",
                    Status: SingleContentAnalysisStatus.ContentResolved,
                    FailureCode: null,
                    CreatedAtUtc: new DateTime(2026, 5, 27, 8, 0, 0, DateTimeKind.Utc),
                    UpdatedAtUtc: new DateTime(2026, 5, 27, 8, 1, 0, DateTimeKind.Utc),
                    CompletedAtUtc: null,
                    LatestContentGeneration: null),
            ],
            TotalCount: 2,
            Page: 1,
            PageSize: 20));

        var handler = new ListSingleContentAnalysesHandler(repo);
        var result = await handler.Handle(
            new ListSingleContentAnalysesQuery(OrgId, UserId, Page: 1, PageSize: 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrgId, repo.LastOrganizationId);
        Assert.Equal(UserId, repo.LastUserId);
        Assert.Equal(1, repo.LastPage);
        Assert.Equal(20, repo.LastPageSize);

        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);

        // First item has a Completed content-generation with a package → must surface as
        // a non-null summary so the UI can render a "download" button.
        var first = result.Value.Items[0];
        Assert.NotNull(first.LatestContentGeneration);
        Assert.Equal(jobId, first.LatestContentGeneration!.JobId);
        Assert.Equal("Completed", first.LatestContentGeneration.Status);
        Assert.True(first.LatestContentGeneration.HasPackage);

        // Second item has no generation job → null summary, no download.
        var second = result.Value.Items[1];
        Assert.Null(second.LatestContentGeneration);
    }

    [Fact]
    public async Task Handle_PropagatesPagingParameters()
    {
        var repo = new SpyRepository(new SingleContentAnalysisHistoryPage([], 0, 3, 50));
        var handler = new ListSingleContentAnalysesHandler(repo);

        await handler.Handle(
            new ListSingleContentAnalysesQuery(OrgId, UserId, Page: 3, PageSize: 50),
            CancellationToken.None);

        Assert.Equal(3, repo.LastPage);
        Assert.Equal(50, repo.LastPageSize);
    }

    // ─── Test double ─────────────────────────────────────────────────────────

    private sealed class SpyRepository : ISingleContentAnalysisRepository
    {
        private readonly SingleContentAnalysisHistoryPage _page;

        public Guid LastOrganizationId { get; private set; }
        public Guid LastUserId { get; private set; }
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }

        public SpyRepository(SingleContentAnalysisHistoryPage page) => _page = page;

        public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(
            Guid organizationId, Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            LastOrganizationId = organizationId;
            LastUserId = userId;
            LastPage = page;
            LastPageSize = pageSize;
            return Task.FromResult(_page);
        }

        public Task AddAsync(SingleContentAnalysisRecord analysis, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<SingleContentAnalysisRecord?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct = default) =>
            Task.FromResult<SingleContentAnalysisRecord?>(null);
        public Task UpdateStatusAsync(Guid id, Guid orgId, SingleContentAnalysisStatus status, DateTime updatedAt,
            CancellationToken ct = default, string? failureCode = null, string? failureKey = null) =>
            Task.CompletedTask;
        public Task UpdateAsync(SingleContentAnalysisRecord a, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(
            Guid id, Guid orgId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>?>(null);
    }
}
