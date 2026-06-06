using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;

/// <summary>
/// Recovery policy for content-generation jobs that never reached a terminal status — the
/// mechanism that stops the history pill from showing "generating" forever.
///
/// <para>
/// Pure application logic (no hosting/scheduling concerns): the background worker decides WHEN to
/// call these (at startup and on a periodic timer); this type decides WHICH jobs to fail.
/// </para>
/// </summary>
public static class ContentGenerationJobWatchdog
{
    /// <summary>A "Running" job older than this is considered stuck and is force-failed by the
    /// periodic sweep. Set above the worker's per-job timeout so the per-job timeout always gets
    /// first chance to record an accurate terminal status.</summary>
    public static readonly TimeSpan StuckJobThreshold = TimeSpan.FromMinutes(16);

    /// <summary>
    /// Startup recovery: fail EVERY job still in "Running". These are orphans from a previous
    /// process that crashed mid-job — no worker is processing them anymore. Returns the count failed.
    /// </summary>
    public static async Task<int> MarkOrphanedRunningJobsAsync(
        IContentGenerationJobRepository jobRepo,
        CancellationToken ct)
    {
        var jobs = await jobRepo.ListPendingOrRunningAsync(ct);
        var failed = 0;

        foreach (var orphan in jobs.Where(j => j.Status == "Running"))
        {
            await jobRepo.UpdateStatusAsync(orphan.Id, "Failed", "restart-detected", "restart-detected", ct);
            failed++;
        }

        return failed;
    }

    /// <summary>
    /// Periodic sweep: fail every job stuck in "Running" whose start time is older than
    /// <see cref="StuckJobThreshold"/> relative to <paramref name="nowUtc"/>. Backstop for a worker
    /// that died without running its own cancellation handler. Returns the count failed.
    /// </summary>
    public static async Task<int> FailStuckRunningJobsAsync(
        IContentGenerationJobRepository jobRepo,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var jobs = await jobRepo.ListPendingOrRunningAsync(ct);
        var failed = 0;

        foreach (var job in jobs.Where(j => j.Status == "Running"))
        {
            // StartedAt is set when the job transitions to Running; fall back to UpdatedAt.
            var startedAt = job.StartedAt ?? job.UpdatedAt;
            if (nowUtc - startedAt <= StuckJobThreshold)
            {
                continue;
            }

            await jobRepo.UpdateStatusAsync(job.Id, "Failed", "watchdog-timeout", "watchdog-timeout", ct);
            failed++;
        }

        return failed;
    }
}
