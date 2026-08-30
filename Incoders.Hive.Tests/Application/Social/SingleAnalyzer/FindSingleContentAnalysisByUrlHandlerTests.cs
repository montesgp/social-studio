using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.SingleAnalyzer.History;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Application.Social.SingleAnalyzer;

public sealed class FindSingleContentAnalysisByUrlHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Handle_NormalizesUrlAndReturnsNewestHistoryMatch()
    {
        var match = MakeHistoryItem();
        var repo = new SpyRepository(match);
        var handler = new FindSingleContentAnalysisByUrlHandler(repo);

        var result = await handler.Handle(
            new FindSingleContentAnalysisByUrlQuery(
                OrgId,
                UserId,
                "https://www.tiktok.com/@brand/video/123#comments"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Item);
        Assert.Equal(match.Id, result.Value.Item!.Id);
        Assert.Equal(OrgId, repo.LastOrganizationId);
        Assert.Equal(UserId, repo.LastUserId);
        Assert.Equal("https://www.tiktok.com/@brand/video/123", repo.LastNormalizedUrl);
        Assert.Equal("https://www.tiktok.com/@brand/video/123#comments", repo.LastSourceUrl);
    }

    [Fact]
    public async Task Handle_InvalidUrlReturnsValidationFailureAndDoesNotQueryRepository()
    {
        var repo = new SpyRepository(null);
        var handler = new FindSingleContentAnalysisByUrlHandler(repo);

        var result = await handler.Handle(
            new FindSingleContentAnalysisByUrlQuery(OrgId, UserId, "not-a-url"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SingleAnalyzerErrors.UrlInvalid.Code, result.Error.Code);
        Assert.False(repo.WasCalled);
    }

    private static SingleContentAnalysisHistoryItem MakeHistoryItem()
    {
        var jobId = Guid.NewGuid();
        return new SingleContentAnalysisHistoryItem(
            Id: Guid.NewGuid(),
            ClientId: Guid.NewGuid(),
            SourceUrl: "https://www.tiktok.com/@brand/video/123",
            NormalizedUrl: "https://www.tiktok.com/@brand/video/123",
            SourcePlatformKey: "tiktok",
            Status: SingleContentAnalysisStatus.Analyzed,
            FailureCode: null,
            CreatedAtUtc: new DateTime(2026, 5, 28, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc: new DateTime(2026, 5, 28, 10, 1, 0, DateTimeKind.Utc),
            CompletedAtUtc: new DateTime(2026, 5, 28, 10, 1, 0, DateTimeKind.Utc),
            LatestContentGeneration: new ContentGenerationSummary(
                jobId,
                "Completed",
                HasPackage: true,
                PackageJobId: jobId,
                CompletedAt: new DateTime(2026, 5, 28, 10, 2, 0, DateTimeKind.Utc)));
    }

    private sealed class SpyRepository : ISingleContentAnalysisRepository
    {
        private readonly SingleContentAnalysisHistoryItem? _match;

        public SpyRepository(SingleContentAnalysisHistoryItem? match) => _match = match;

        public bool WasCalled { get; private set; }
        public Guid LastOrganizationId { get; private set; }
        public Guid LastUserId { get; private set; }
        public string? LastNormalizedUrl { get; private set; }
        public string? LastSourceUrl { get; private set; }

        public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(
            Guid organizationId,
            Guid userId,
            string normalizedUrl,
            string sourceUrl,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastOrganizationId = organizationId;
            LastUserId = userId;
            LastNormalizedUrl = normalizedUrl;
            LastSourceUrl = sourceUrl;
            return Task.FromResult(_match);
        }

        public Task AddAsync(SingleContentAnalysisRecord analysis, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<SingleContentAnalysisRecord?> GetByIdAsync(
            Guid analysisId,
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SingleContentAnalysisRecord?>(null);

        public Task UpdateStatusAsync(
            Guid analysisId,
            Guid organizationId,
            SingleContentAnalysisStatus status,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken = default,
            string? failureCode = null,
            string? failureMessageKey = null) =>
            Task.CompletedTask;

        public Task UpdateAsync(
            SingleContentAnalysisRecord analysis,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(
            Guid organizationId,
            Guid userId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SingleContentAnalysisHistoryPage([], 0, page, pageSize));

        public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(
            Guid analysisId,
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>?>(null);
    }
}
