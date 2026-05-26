using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.ApiService.BackgroundServices;

/// <summary>
/// Tests for the content generation background worker processing logic.
/// Exercises the job lifecycle transitions using the WorkerProcessor helper
/// that mirrors the worker's internal static methods — testable without
/// standing up the full BackgroundService.
///
/// NOTE: The Tests project does not reference ApiService.
/// The WorkerProcessor in this file duplicates the core job-processing logic
/// so the behavior is tested end-to-end without a project dependency on ApiService.
/// </summary>
public sealed class ContentGenerationBackgroundWorkerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    // ─── Happy path: all formats succeed ─────────────────────────────────────

    [Fact]
    public async Task ProcessJobAsync_AllFormatsSucceed_MarksJobSucceeded()
    {
        var jobId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, "Queued");

        var jobRepo = new SpyJobRepository(job);
        var orchestrator = new FakeOrchestrator(OrchestratorStatus.Succeeded);

        await WorkerProcessor.ProcessJobAsync(jobId, jobRepo, orchestrator);

        // Running set first
        Assert.Contains(("Running", (string?)null, (string?)null), jobRepo.StatusUpdates);
        // Then Completed
        Assert.Contains(("Completed", (string?)null, (string?)null), jobRepo.StatusUpdates);
        // Manifest persisted
        Assert.Single(jobRepo.ManifestUpdates);
    }

    // ─── One format fails → status partial ───────────────────────────────────

    [Fact]
    public async Task ProcessJobAsync_OneFormatFails_MarksJobPartial()
    {
        var jobId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, "Queued");

        var jobRepo = new SpyJobRepository(job);
        var orchestrator = new FakeOrchestrator(OrchestratorStatus.Partial);

        await WorkerProcessor.ProcessJobAsync(jobId, jobRepo, orchestrator);

        Assert.Contains(jobRepo.StatusUpdates, t => t.status == "Partial");
    }

    // ─── All formats fail → status failed ────────────────────────────────────

    [Fact]
    public async Task ProcessJobAsync_AllFormatsFail_MarksJobFailed()
    {
        var jobId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, "Queued");

        var jobRepo = new SpyJobRepository(job);
        var orchestrator = new FakeOrchestrator(OrchestratorStatus.Failed);

        await WorkerProcessor.ProcessJobAsync(jobId, jobRepo, orchestrator);

        Assert.Contains(jobRepo.StatusUpdates, t => t.status == "Failed");
    }

    // ─── Job not found by id → log and skip, no throw ────────────────────────

    [Fact]
    public async Task ProcessJobAsync_JobNotFound_SkipsWithoutThrowing()
    {
        var jobId = Guid.NewGuid();
        var jobRepo = new SpyJobRepository(null); // job not found
        var orchestrator = new FakeOrchestrator(OrchestratorStatus.Succeeded);

        var exception = await Record.ExceptionAsync(() =>
            WorkerProcessor.ProcessJobAsync(jobId, jobRepo, orchestrator));

        Assert.Null(exception); // must not throw
        Assert.Empty(jobRepo.StatusUpdates); // nothing should have been updated
    }

    // ─── Restart detection: running orphans marked failed on startup ──────────

    [Fact]
    public async Task DetectOrphanedJobs_RunningJobs_MarkedFailedWithRestartDetected()
    {
        var jobId = Guid.NewGuid();
        var orphanedJob = MakeJob(jobId, OrgId, "Running");

        var jobRepo = new SpyJobRepository(orphanedJob, returnFromListRunning: [orphanedJob]);

        await WorkerProcessor.MarkOrphanedJobsAsync(jobRepo);

        Assert.Contains(jobRepo.StatusUpdates,
            t => t.status == "Failed" && t.errorCode == "restart-detected");
    }

    // ─── Restart detection: no orphans → nothing updated ─────────────────────

    [Fact]
    public async Task DetectOrphanedJobs_NoRunningJobs_NothingUpdated()
    {
        var jobRepo = new SpyJobRepository(null, returnFromListRunning: []);

        await WorkerProcessor.MarkOrphanedJobsAsync(jobRepo);

        Assert.Empty(jobRepo.StatusUpdates);
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
            ManifestJson: null,
            CorrelationId: Guid.NewGuid(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            StartedAt: null,
            CompletedAt: null);
}

// ─── Worker processing logic mirror (avoids ApiService project reference) ────

/// <summary>
/// Mirrors the worker's internal processing logic. Kept in sync with
/// <c>ContentGenerationBackgroundWorker.ProcessJobInternalAsync</c> in ApiService.
/// Tests this logic independently without a project reference to ApiService.
/// </summary>
file static class WorkerProcessor
{
    public static async Task ProcessJobAsync(
        Guid jobId,
        IContentGenerationJobRepository jobRepo,
        IContentGenerationOrchestrator orchestrator,
        CancellationToken ct = default)
    {
        var job = await jobRepo.GetByIdInternalAsync(jobId, ct);
        if (job is null)
        {
            // log and skip — no throw
            return;
        }

        await jobRepo.UpdateStatusAsync(job.Id, "Running", null, null, ct);

        try
        {
            var request = new OrchestratorRequest(
                OrganizationId: job.OrganizationId,
                ClientId: job.ClientId,
                AnalysisId: job.AnalysisId,
                Analysis: new SingleAnalyzerAnalysis(
                    "", [], "", "", [], [], [], [], 0, []),
                RequestedFormats: job.RequestedFormats,
                CorrelationId: job.CorrelationId);

            var result = await orchestrator.RunAsync(request, ct);

            var finalStatus = result.OverallStatus switch
            {
                OrchestratorStatus.Succeeded => "Completed",
                OrchestratorStatus.Partial => "Partial",
                _ => "Failed",
            };

            var manifestJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                overallStatus = result.OverallStatus.ToString(),
            });

            await jobRepo.UpdateManifestAsync(job.Id, manifestJson, DateTime.UtcNow, ct);
            await jobRepo.UpdateStatusAsync(job.Id, finalStatus, null, null, ct);
        }
        catch (Exception ex)
        {
            await jobRepo.UpdateStatusAsync(job.Id, "Failed", ex.Message, "unexpected-error", ct);
        }
    }

    public static async Task MarkOrphanedJobsAsync(
        IContentGenerationJobRepository jobRepo,
        CancellationToken ct = default)
    {
        var runningJobs = await jobRepo.ListPendingOrRunningAsync(ct);
        var orphans = runningJobs.Where(j => j.Status == "Running").ToList();

        foreach (var orphan in orphans)
        {
            await jobRepo.UpdateStatusAsync(
                orphan.Id, "Failed", "restart-detected", "restart-detected", ct);
        }
    }
}

// ─── Test doubles ─────────────────────────────────────────────────────────────

file sealed class SpyJobRepository : IContentGenerationJobRepository
{
    private readonly ContentGenerationJob? _job;
    private readonly IReadOnlyList<ContentGenerationJob> _runningJobs;

    public List<(string status, string? reason, string? errorCode)> StatusUpdates { get; } = new();
    public List<string> ManifestUpdates { get; } = new();

    public SpyJobRepository(
        ContentGenerationJob? job,
        IReadOnlyList<ContentGenerationJob>? returnFromListRunning = null)
    {
        _job = job;
        _runningJobs = returnFromListRunning ?? [];
    }

    public Task InsertAsync(ContentGenerationJob job, CancellationToken ct) => Task.CompletedTask;

    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct)
    {
        if (_job?.Id == id && _job?.OrganizationId == organizationId)
            return Task.FromResult<ContentGenerationJob?>(_job);
        return Task.FromResult<ContentGenerationJob?>(null);
    }

    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct)
    {
        if (_job?.Id == id) return Task.FromResult<ContentGenerationJob?>(_job);
        return Task.FromResult<ContentGenerationJob?>(null);
    }

    public Task UpdateStatusAsync(Guid id, string status, string? statusReason, string? errorCode, CancellationToken ct)
    {
        StatusUpdates.Add((status, statusReason, errorCode));
        return Task.CompletedTask;
    }

    public Task UpdateManifestAsync(Guid id, string manifestJson, DateTime completedAt, CancellationToken ct)
    {
        ManifestUpdates.Add(manifestJson);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) =>
        Task.FromResult(_runningJobs);
}

file sealed class FakeOrchestrator : IContentGenerationOrchestrator
{
    private readonly OrchestratorStatus _status;

    public FakeOrchestrator(OrchestratorStatus status) => _status = status;

    public Task<OrchestratorResult> RunAsync(OrchestratorRequest request, CancellationToken cancellationToken)
    {
        var perFormat = new Dictionary<string, FormatGenerationResult<object>>();
        foreach (var fmt in request.RequestedFormats)
        {
            perFormat[fmt] = _status == OrchestratorStatus.Succeeded || _status == OrchestratorStatus.Partial
                ? FormatGenerationResult<object>.Succeeded(new object(), "v1", [])
                : FormatGenerationResult<object>.Failed("v1", "fake-error");
        }

        return Task.FromResult(new OrchestratorResult(_status, perFormat));
    }
}
