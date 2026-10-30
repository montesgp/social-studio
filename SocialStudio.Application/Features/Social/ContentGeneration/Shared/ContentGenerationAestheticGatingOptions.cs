namespace SocialStudio.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Configuration for the first-time <c>ClientAesthetic</c> profile-analysis gating introduced by
/// issue #67 (BAPA-CG-01/02). Bound from configuration section
/// <c>ContentGeneration:AestheticGating</c>.
///
/// <para>
/// INVARIANT: <see cref="MaxWaitSeconds"/> MUST stay strictly below the configured
/// <c>ContentGenerationWorkerOptions.JobTimeout</c> (default 3600s / 60min, see
/// <see cref="ContentGenerationWorkerOptions.DefaultJobTimeoutMinutes"/>) so the gating wait always
/// resolves to a clean, actionable <c>Failed</c> write before the outer per-job timeout fires. The
/// job-timeout ceiling is itself kept below <c>ContentGenerationJobWatchdog.StuckJobThreshold</c>
/// (default 3720s / 62min) — enforced at worker startup via
/// <c>ContentGenerationJobWatchdog.EnsureJobTimeoutBelowStuckThreshold</c>. See design.md
/// "Timeout / Budget Alignment". A future bump to <see cref="MaxWaitSeconds"/> MUST leave real
/// headroom below the configured job timeout.
/// </para>
/// <para>
/// The <see cref="MaxWaitSeconds"/> clock starts when
/// <c>EnsureAvailableBlockingAsync</c>'s poll loop begins (own account resolved, analysis
/// enqueued/in-flight) — NOT at job start. The job's own <c>JobTimeout</c> clock, by contrast,
/// starts at job start and already covers the brand-load / analysis-fetch / own-account pre-loop
/// work that runs BEFORE the wait begins. Leave real headroom below <c>JobTimeout</c> when tuning
/// this value — the pre-loop time is not "free" budget on top of it.
/// </para>
/// </summary>
public sealed class ContentGenerationAestheticGatingOptions
{
    public const string SectionName = "ContentGeneration:AestheticGating";

    public const int DefaultPollIntervalSeconds = 2;
    public const int DefaultMaxWaitSeconds = 600;

    /// <summary>
    /// Feature toggle. <c>true</c> (default): first-time/no-usable-aesthetic ClientAesthetic jobs
    /// block on the enqueued analysis per BAPA-CG-01/02. <c>false</c>: rollback safety valve —
    /// restores the legacy fire-and-forget behavior everywhere (temporary mode only, see
    /// design.md "Residual Risks" — leaves the pre-#67 silent-degradation bug latent).
    /// </summary>
    public bool BlockOnFirstTime { get; set; } = true;

    /// <summary>Interval between polls of the analysis repository while waiting. Default 2s.</summary>
    public int PollIntervalSeconds { get; set; } = DefaultPollIntervalSeconds;

    /// <summary>
    /// Bounded ceiling for the blocking wait. Default 600s (10 min). MUST stay strictly below the
    /// content-generation job budget (900s) — see the type-level invariant note above.
    /// </summary>
    public int MaxWaitSeconds { get; set; } = DefaultMaxWaitSeconds;
}
