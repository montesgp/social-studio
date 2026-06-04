using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Incoders.Hive.Tests.Common;
using DomainProfileAnalysis = Incoders.Hive.Domain.Social.ProfileAnalysis.ProfileAnalysis;

namespace Incoders.Hive.Tests.Application.Social.ProfileAnalyzer;

/// <summary>
/// TDD unit tests for <see cref="RetryProfileAnalysisHandler"/>.
/// Covers: Failed+scrape → RestartAnalysis+enqueue; Failed-without-scrape → 422;
/// non-Failed → 422; cross-org → not found.
/// </summary>
public sealed class RetryProfileAnalysisHandlerTests
{
    private static readonly DateTime Now = new(2026, 6, 3, 10, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ── Happy path: Failed + complete scrape data → RestartAnalysis + enqueue RetryAi ──

    [Fact]
    public async Task Handle_FailedWithScrapeData_TransitionsToAnalyzingAndEnqueuesRetryAiJob()
    {
        var orgId = Guid.NewGuid();
        var harness = new Harness(Now);
        var analysis = MakeFailedWithScrapeData(Guid.NewGuid(), orgId);
        harness.Repository.Store(analysis);

        var result = await harness.Handler.Handle(
            new RetryProfileAnalysisCommand(analysis.Id, orgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error?.Code}");
        Assert.Equal("Analyzing", result.Value.Status);
        Assert.Null(result.Value.FailureCode);

        // Persisted record must be Analyzing
        Assert.Equal(ProfileAnalysisStatus.Analyzing, harness.Repository.Stored!.Status);
        Assert.Null(harness.Repository.Stored.FailureCode);

        // One RetryAi job must have been enqueued
        Assert.Single(harness.Queue.Enqueued);
        Assert.Equal(analysis.Id, harness.Queue.Enqueued[0].AnalysisId);
        Assert.Equal(orgId, harness.Queue.Enqueued[0].OrganizationId);
        Assert.Equal(ProfileAnalysisJobKind.RetryAi, harness.Queue.Enqueued[0].Kind);
    }

    [Fact]
    public async Task Handle_FailedWithScrapeData_SavesChangesToUnitOfWork()
    {
        var orgId = Guid.NewGuid();
        var harness = new Harness(Now);
        harness.Repository.Store(MakeFailedWithScrapeData(Guid.NewGuid(), orgId));

        await harness.Handler.Handle(
            new RetryProfileAnalysisCommand(harness.Repository.Stored!.Id, orgId),
            CancellationToken.None);

        Assert.True(harness.UnitOfWork.SaveChangesCalled);
    }

    // ── Rejected: Failed WITHOUT scrape data → NotResumable ───────────────────

    [Fact]
    public async Task Handle_FailedWithoutScrapeData_ReturnsNotResumable()
    {
        var orgId = Guid.NewGuid();
        var harness = new Harness(Now);
        var analysis = MakeFailedNoScrapeData(Guid.NewGuid(), orgId);
        harness.Repository.Store(analysis);

        var result = await harness.Handler.Handle(
            new RetryProfileAnalysisCommand(analysis.Id, orgId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.profile_analysis.not_resumable", result.Error.Code);
        Assert.Empty(harness.Queue.Enqueued);
    }

    [Fact]
    public async Task Handle_FailedWithPartialScrapeData_ReturnsNotResumable()
    {
        // Missing ServerDerivedStatsJson — partial scrape: should still be rejected.
        var orgId = Guid.NewGuid();
        var harness = new Harness(Now);
        var analysis = MakeFailedPartialScrapeData(Guid.NewGuid(), orgId);
        harness.Repository.Store(analysis);

        var result = await harness.Handler.Handle(
            new RetryProfileAnalysisCommand(analysis.Id, orgId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.profile_analysis.not_resumable", result.Error.Code);
        Assert.Empty(harness.Queue.Enqueued);
    }

    // ── Rejected: non-Failed status → NotResumable ────────────────────────────

    [Fact]
    public async Task Handle_AnalyzedRecord_ReturnsNotResumable()
    {
        var orgId = Guid.NewGuid();
        var harness = new Harness(Now);
        var analysis = MakeAnalyzed(Guid.NewGuid(), orgId);
        harness.Repository.Store(analysis);

        var result = await harness.Handler.Handle(
            new RetryProfileAnalysisCommand(analysis.Id, orgId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.profile_analysis.not_resumable", result.Error.Code);
        Assert.Empty(harness.Queue.Enqueued);
    }

    [Fact]
    public async Task Handle_RequestedRecord_ReturnsNotResumable()
    {
        var orgId = Guid.NewGuid();
        var harness = new Harness(Now);
        var analysis = DomainProfileAnalysis.CreateFromUrl(orgId, "https://instagram.com/test", Now);
        harness.Repository.Store(analysis);

        var result = await harness.Handler.Handle(
            new RetryProfileAnalysisCommand(analysis.Id, orgId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.profile_analysis.not_resumable", result.Error.Code);
    }

    // ── Rejected: cross-org / not found → NotFound ────────────────────────────

    [Fact]
    public async Task Handle_WrongOrgId_ReturnsNotFound()
    {
        var orgId = Guid.NewGuid();
        var otherOrgId = Guid.NewGuid();
        var harness = new Harness(Now);
        harness.Repository.Store(MakeFailedWithScrapeData(Guid.NewGuid(), orgId));

        var result = await harness.Handler.Handle(
            new RetryProfileAnalysisCommand(harness.Repository.Stored!.Id, otherOrgId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.profile_analysis.not_found", result.Error.Code);
        Assert.Empty(harness.Queue.Enqueued);
    }

    [Fact]
    public async Task Handle_UnknownAnalysisId_ReturnsNotFound()
    {
        var orgId = Guid.NewGuid();
        var harness = new Harness(Now);
        // Repository is empty

        var result = await harness.Handler.Handle(
            new RetryProfileAnalysisCommand(Guid.NewGuid(), orgId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.profile_analysis.not_found", result.Error.Code);
    }

    // ── Verify enqueued job kind is RetryAi (not Full) ────────────────────────

    [Fact]
    public async Task Handle_SuccessPath_EnqueuesRetryAiKindJob_NotFullKind()
    {
        var orgId = Guid.NewGuid();
        var harness = new Harness(Now);
        harness.Repository.Store(MakeFailedWithScrapeData(Guid.NewGuid(), orgId));

        await harness.Handler.Handle(
            new RetryProfileAnalysisCommand(harness.Repository.Stored!.Id, orgId),
            CancellationToken.None);

        Assert.NotEqual(ProfileAnalysisJobKind.Full, harness.Queue.Enqueued[0].Kind);
        Assert.Equal(ProfileAnalysisJobKind.RetryAi, harness.Queue.Enqueued[0].Kind);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DomainProfileAnalysis MakeFailedWithScrapeData(Guid id, Guid orgId) =>
        DomainProfileAnalysis.Reconstitute(
            id: id,
            organizationId: orgId,
            clientId: null,
            ownAccountId: null,
            profileUrlOrHandle: "https://instagram.com/testbrand",
            status: ProfileAnalysisStatus.Failed,
            rawPostsDatasetJson: JsonSerializer.Serialize(new[] { new { id = "p1" } }, JsonOpts),
            serverDerivedStatsJson: JsonSerializer.Serialize(new { er = 3.5 }, JsonOpts),
            profileSnapshotJson: JsonSerializer.Serialize(new { instagramId = "12345" }, JsonOpts),
            aiAnalysisJson: null,
            providerDiagnosticsJson: "{}",
            failureCode: "ai.provider_error",
            createdAtUtc: Now,
            updatedAtUtc: Now,
            completedAtUtc: Now);

    private static DomainProfileAnalysis MakeFailedNoScrapeData(Guid id, Guid orgId) =>
        DomainProfileAnalysis.Reconstitute(
            id: id,
            organizationId: orgId,
            clientId: null,
            ownAccountId: null,
            profileUrlOrHandle: "https://instagram.com/testbrand",
            status: ProfileAnalysisStatus.Failed,
            rawPostsDatasetJson: null,
            serverDerivedStatsJson: null,
            profileSnapshotJson: null,
            aiAnalysisJson: null,
            providerDiagnosticsJson: "{}",
            failureCode: "integration.apify_not_configured",
            createdAtUtc: Now,
            updatedAtUtc: Now,
            completedAtUtc: Now);

    private static DomainProfileAnalysis MakeFailedPartialScrapeData(Guid id, Guid orgId) =>
        DomainProfileAnalysis.Reconstitute(
            id: id,
            organizationId: orgId,
            clientId: null,
            ownAccountId: null,
            profileUrlOrHandle: "https://instagram.com/testbrand",
            status: ProfileAnalysisStatus.Failed,
            rawPostsDatasetJson: JsonSerializer.Serialize(new[] { new { id = "p1" } }, JsonOpts),
            serverDerivedStatsJson: null,          // missing
            profileSnapshotJson: JsonSerializer.Serialize(new { instagramId = "12345" }, JsonOpts),
            aiAnalysisJson: null,
            providerDiagnosticsJson: "{}",
            failureCode: "ai.provider_error",
            createdAtUtc: Now,
            updatedAtUtc: Now,
            completedAtUtc: Now);

    private static DomainProfileAnalysis MakeAnalyzed(Guid id, Guid orgId) =>
        DomainProfileAnalysis.Reconstitute(
            id: id,
            organizationId: orgId,
            clientId: null,
            ownAccountId: null,
            profileUrlOrHandle: "https://instagram.com/testbrand",
            status: ProfileAnalysisStatus.Analyzed,
            rawPostsDatasetJson: "[]",
            serverDerivedStatsJson: "{}",
            profileSnapshotJson: "{}",
            aiAnalysisJson: "{}",
            providerDiagnosticsJson: "{}",
            failureCode: null,
            createdAtUtc: Now,
            updatedAtUtc: Now,
            completedAtUtc: Now);

    // ── Harness ───────────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public Harness(DateTime nowUtc)
        {
            Clock = new TestClock(nowUtc);
            Repository = new FakeRepository();
            UnitOfWork = new FakeUnitOfWork();
            Queue = new FakeJobQueue();
            Handler = new RetryProfileAnalysisHandler(Repository, UnitOfWork, Clock, Queue);
        }

        public TestClock Clock { get; }
        public FakeRepository Repository { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public FakeJobQueue Queue { get; }
        public RetryProfileAnalysisHandler Handler { get; }
    }

    private sealed class FakeRepository : IProfileAnalysisRepository
    {
        private DomainProfileAnalysis? _stored;

        public DomainProfileAnalysis? Stored => _stored;

        public void Store(DomainProfileAnalysis analysis) => _stored = analysis;

        public Task AddAsync(DomainProfileAnalysis analysis, CancellationToken cancellationToken = default)
        {
            _stored = analysis;
            return Task.CompletedTask;
        }

        public Task<DomainProfileAnalysis?> GetByIdAsync(
            Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _stored?.Id == analysisId && _stored.OrganizationId == organizationId
                    ? _stored : null);

        public Task<ProfileAnalysisHistoryPage> GetByOrganizationAsync(
            Guid organizationId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProfileAnalysisHistoryPage([], 0, page, pageSize));

        public Task UpdateAsync(DomainProfileAnalysis analysis, CancellationToken cancellationToken = default)
        {
            _stored = analysis;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(
            Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public bool SaveChangesCalled { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeJobQueue : IProfileAnalysisJobQueue
    {
        public List<ProfileAnalysisJob> Enqueued { get; } = [];

        public ValueTask EnqueueAsync(ProfileAnalysisJob job, CancellationToken cancellationToken = default)
        {
            Enqueued.Add(job);
            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<ProfileAnalysisJob> DequeueAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
