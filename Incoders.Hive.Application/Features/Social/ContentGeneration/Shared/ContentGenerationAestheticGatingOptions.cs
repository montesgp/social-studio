namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Configuration for the first-time <c>ClientAesthetic</c> profile-analysis gating introduced by
/// issue #67 (BAPA-CG-01/02). Bound from configuration section
/// <c>ContentGeneration:AestheticGating</c>.
///
/// <para>
/// INVARIANT: <see cref="MaxWaitSeconds"/> MUST stay strictly below
/// <c>ContentGenerationBackgroundWorker.JobTimeout</c> (900s / 15min) so the gating wait always
/// resolves to a clean, actionable <c>Failed</c> write before the outer per-job timeout fires. The
/// 900s ceiling is itself below <c>ContentGenerationJobWatchdog.StuckJobThreshold</c> (960s / 16min).
/// See design.md "Timeout / Budget Alignment". A future bump to <see cref="MaxWaitSeconds"/> MUST
/// NOT cross the 15-minute line.
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
