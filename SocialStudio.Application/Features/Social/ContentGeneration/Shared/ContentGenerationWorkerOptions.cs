namespace SocialStudio.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Configuration for <c>ContentGenerationBackgroundWorker</c>'s per-job wall-clock budget. Bound
/// from configuration section <c>ContentGeneration:Worker</c>.
///
/// <para>
/// INVARIANT: <see cref="JobTimeout"/> MUST stay strictly below
/// <c>ContentGenerationJobWatchdog.StuckJobThreshold</c> so the per-job <c>CancelAfter</c> always
/// gets first chance to record an accurate terminal status before the periodic watchdog sweep
/// force-fails the job as "stuck". This is enforced at worker startup via
/// <c>ContentGenerationJobWatchdog.EnsureJobTimeoutBelowStuckThreshold</c> — a misconfiguration
/// (e.g. raising <see cref="JobTimeout"/> past the threshold) fails fast instead of silently
/// reaping healthy jobs.
/// </para>
/// </summary>
public sealed class ContentGenerationWorkerOptions
{
    public const string SectionName = "ContentGeneration:Worker";

    // Raised from 30 to 60 min (issue: job 8757e85a / 07771831): a real carousel generates 3-4
    // ComfyUI background images at ~6.5 min each on non-CUDA reference hardware (DirectML,
    // --lowvram --cpu-vae), which alone can approach or exceed a 30-min budget before any
    // CSS/PPTX rendering happens. Blowing the budget discards ALL completed GPU work — the job
    // is marked Failed with zero persisted assets even though every image succeeded. A faster
    // (CUDA) production backend finishes well inside this budget; this only raises the ceiling
    // for slow hardware, it does not slow down fast hardware.
    public const int DefaultJobTimeoutMinutes = 60;

    /// <summary>
    /// Per-job wall-clock backstop. Generous: a multi-format job legitimately makes several bounded
    /// AI calls plus renders. This only fires for a genuine hang, never normal work. Default: 60
    /// minutes (see <see cref="DefaultJobTimeoutMinutes"/> for the sizing rationale).
    /// </summary>
    public int JobTimeoutMinutes { get; set; } = DefaultJobTimeoutMinutes;

    /// <summary>Convenience accessor — <see cref="JobTimeoutMinutes"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan JobTimeout => TimeSpan.FromMinutes(JobTimeoutMinutes);
}
