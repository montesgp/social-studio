using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.SingleAnalyzer.Analyze;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Application.Social.SingleAnalyzer;

/// <summary>
/// Guards the recovery policy that force-fails single-analyzer analyses stuck in a non-terminal
/// status — the backstop for a job the background worker never picked up (e.g. the in-memory
/// ChannelSingleAnalysisJobQueue entry lost to a process restart, as reproduced in
/// logs/hive-api.log 2026-09-09: a "Requested" analysis whose preflight succeeded but no
/// "PROCESSING job" ever appeared for the rest of the day). Without this sweep, such a record
/// blocks the org-scoped duplicate gate forever with no way for the user to retry.
/// </summary>
public sealed class SingleAnalysisJobWatchdogTests
{
    private static readonly DateTime Now = new(2026, 9, 9, 22, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FailStalled_NonTerminalPastThreshold_IsFailedWithWorkflowStalledCode()
    {
        var stuck = MakeAnalysis(
            SingleContentAnalysisStatus.Requested,
            updatedAtUtc: Now - SingleAnalysisJobWatchdog.StuckThreshold - TimeSpan.FromMinutes(1));
        var repo = new FakeRepo([stuck]);
        var unitOfWork = new FakeUnitOfWork();

        var failed = await SingleAnalysisJobWatchdog.FailStalledAnalysesAsync(repo, unitOfWork, Now, CancellationToken.None);

        Assert.Equal(1, failed);
        var updated = Assert.Single(repo.Updated);
        Assert.Equal(stuck.Id, updated.Id);
        Assert.Equal(SingleContentAnalysisStatus.Failed, updated.Status);
        Assert.Equal(SingleAnalyzerErrors.WorkflowStalled.Code, updated.FailureCode);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task FailStalled_NonTerminalWithinThreshold_IsLeftAlone()
    {
        var fresh = MakeAnalysis(
            SingleContentAnalysisStatus.Analyzing,
            updatedAtUtc: Now - TimeSpan.FromMinutes(1));
        var repo = new FakeRepo([fresh]);
        var unitOfWork = new FakeUnitOfWork();

        var failed = await SingleAnalysisJobWatchdog.FailStalledAnalysesAsync(repo, unitOfWork, Now, CancellationToken.None);

        Assert.Equal(0, failed);
        Assert.Empty(repo.Updated);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task FailStalled_PreservesAnyPersistedSnapshot_SoRetryAiStaysAvailable()
    {
        // A job stuck in Analyzing (content already resolved, extraction cost already paid) must
        // NOT lose its snapshot when the watchdog fails it — the user should be able to retry the
        // AI stage alone afterward, not lose the Apify credits already spent.
        var stuck = MakeAnalysis(
            SingleContentAnalysisStatus.Analyzing,
            updatedAtUtc: Now - SingleAnalysisJobWatchdog.StuckThreshold - TimeSpan.FromMinutes(1)) with
        {
            ContentSnapshotJson = "{\"platformKey\":\"tiktok\"}"
        };
        var repo = new FakeRepo([stuck]);
        var unitOfWork = new FakeUnitOfWork();

        await SingleAnalysisJobWatchdog.FailStalledAnalysesAsync(repo, unitOfWork, Now, CancellationToken.None);

        var updated = Assert.Single(repo.Updated);
        Assert.Equal("{\"platformKey\":\"tiktok\"}", updated.ContentSnapshotJson);
    }

    private static SingleContentAnalysisRecord MakeAnalysis(
        SingleContentAnalysisStatus status,
        DateTime updatedAtUtc) => new(
        Id: Guid.NewGuid(),
        OrganizationId: Guid.NewGuid(),
        ClientId: null,
        UserId: Guid.NewGuid(),
        SourceUrl: "https://www.tiktok.com/@brand/video/123",
        NormalizedUrl: "https://www.tiktok.com/@brand/video/123",
        SourcePlatformKey: "tiktok",
        Status: status,
        ContentSnapshotJson: null,
        AnalysisJson: null,
        ProviderDiagnosticsJson: null,
        FailureCode: null,
        FailureMessageKey: null,
        CreatedAtUtc: updatedAtUtc,
        UpdatedAtUtc: updatedAtUtc,
        CompletedAtUtc: null);

    private sealed class FakeRepo : ISingleContentAnalysisRepository
    {
        private readonly List<SingleContentAnalysisRecord> _nonTerminal;

        public FakeRepo(List<SingleContentAnalysisRecord> nonTerminal) => _nonTerminal = nonTerminal;

        public List<SingleContentAnalysisRecord> Updated { get; } = [];

        public Task<IReadOnlyList<SingleContentAnalysisRecord>> ListNonTerminalAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SingleContentAnalysisRecord>>(_nonTerminal);

        public Task UpdateAsync(SingleContentAnalysisRecord analysis, CancellationToken cancellationToken = default)
        {
            Updated.Add(analysis);
            return Task.CompletedTask;
        }

        public Task AddAsync(SingleContentAnalysisRecord analysis, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SingleContentAnalysisRecord?> GetByIdAsync(
            Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdateStatusAsync(
            Guid analysisId, Guid organizationId, SingleContentAnalysisStatus status, DateTime updatedAtUtc,
            CancellationToken cancellationToken = default, string? failureCode = null, string? failureMessageKey = null) =>
            throw new NotSupportedException();

        public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(
            Guid organizationId, Guid userId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(
            Guid organizationId, Guid userId, string normalizedUrl, string sourceUrl,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(
            Guid analysisId, Guid organizationId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(0);
        }
    }
}
