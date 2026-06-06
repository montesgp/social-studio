using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Pipeline;

/// <summary>
/// Guards the recovery policy that force-fails jobs stuck in "Running" — the mechanism that stops
/// the history pill from showing "generating" forever when a worker dies mid-job without recording
/// a terminal status (or on a restart after a crash).
/// </summary>
public sealed class ContentGenerationJobWatchdogTests
{
    private static readonly DateTime Now = new(2026, 06, 06, 12, 00, 00, DateTimeKind.Utc);

    [Fact]
    public async Task FailStuckRunningJobs_RunningPastThreshold_IsFailedWithWatchdogReason()
    {
        var stuck = MakeJob(status: "Running",
            startedAt: Now - ContentGenerationJobWatchdog.StuckJobThreshold - TimeSpan.FromMinutes(5));
        var repo = new FakeJobRepo([stuck]);

        var failed = await ContentGenerationJobWatchdog.FailStuckRunningJobsAsync(repo, Now, CancellationToken.None);

        Assert.Equal(1, failed);
        Assert.Single(repo.StatusUpdates);
        Assert.Equal((stuck.Id, "Failed", "watchdog-timeout"), repo.StatusUpdates[0]);
    }

    [Fact]
    public async Task FailStuckRunningJobs_RunningWithinThreshold_IsLeftAlone()
    {
        var fresh = MakeJob(status: "Running", startedAt: Now - TimeSpan.FromMinutes(1));
        var repo = new FakeJobRepo([fresh]);

        var failed = await ContentGenerationJobWatchdog.FailStuckRunningJobsAsync(repo, Now, CancellationToken.None);

        Assert.Equal(0, failed);
        Assert.Empty(repo.StatusUpdates);
    }

    [Fact]
    public async Task FailStuckRunningJobs_NonRunningJob_IsIgnored()
    {
        // A Queued job is not "stuck running" — the watchdog must not touch it.
        var queued = MakeJob(status: "Queued", startedAt: null);
        var repo = new FakeJobRepo([queued]);

        var failed = await ContentGenerationJobWatchdog.FailStuckRunningJobsAsync(repo, Now, CancellationToken.None);

        Assert.Equal(0, failed);
        Assert.Empty(repo.StatusUpdates);
    }

    [Fact]
    public async Task MarkOrphanedRunningJobs_FailsEveryRunningJob_RegardlessOfAge()
    {
        // Startup recovery: any Running job is an orphan (no worker owns it after a crash).
        var young = MakeJob(status: "Running", startedAt: Now - TimeSpan.FromSeconds(30));
        var old = MakeJob(status: "Running", startedAt: Now - TimeSpan.FromHours(2));
        var queued = MakeJob(status: "Queued", startedAt: null);
        var repo = new FakeJobRepo([young, old, queued]);

        var failed = await ContentGenerationJobWatchdog.MarkOrphanedRunningJobsAsync(repo, CancellationToken.None);

        Assert.Equal(2, failed);
        Assert.All(repo.StatusUpdates, u => Assert.Equal("restart-detected", u.errorCode));
        Assert.DoesNotContain(repo.StatusUpdates, u => u.id == queued.Id);
    }

    private static ContentGenerationJob MakeJob(string status, DateTime? startedAt) => new(
        Id: Guid.NewGuid(),
        OrganizationId: Guid.NewGuid(),
        ClientId: Guid.NewGuid(),
        AnalysisId: Guid.NewGuid(),
        RequestedFormats: ["flyer"],
        Status: status,
        StatusReason: null,
        ErrorCode: null,
        BrandSnapshotJson: "{}",
        ManifestJson: null,
        CorrelationId: Guid.NewGuid(),
        CreatedAt: startedAt ?? Now,
        UpdatedAt: startedAt ?? Now,
        StartedAt: startedAt,
        CompletedAt: null);
}

file sealed class FakeJobRepo(IReadOnlyList<ContentGenerationJob> jobs) : IContentGenerationJobRepository
{
    public List<(Guid id, string status, string? errorCode)> StatusUpdates { get; } = new();

    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) =>
        Task.FromResult(jobs);

    public Task UpdateStatusAsync(Guid id, string status, string? statusReason, string? errorCode, CancellationToken ct)
    {
        StatusUpdates.Add((id, status, errorCode));
        return Task.CompletedTask;
    }

    public Task InsertAsync(ContentGenerationJob job, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
    public Task UpdateManifestAsync(Guid id, string manifestJson, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
}
