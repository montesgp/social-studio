namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Outcome of a single <see cref="IClientAestheticEnsureFreshService.EnsureFreshAsync"/> call.
/// Returned for structured logging and telemetry; the caller MUST NOT branch on this value
/// for generation logic (aesthetic guidance degrades gracefully regardless of outcome).
/// </summary>
public enum EnsureFreshOutcome
{
    /// <summary>The cached analysis is within the effective staleness threshold — no refresh needed.</summary>
    Fresh,

    /// <summary>The analysis was stale or missing; a new analysis job was enqueued in the background.</summary>
    RefreshEnqueued,

    /// <summary>An analysis is already in flight for this client — refresh skipped (debounce, OQ-8).</summary>
    SkippedInFlight,

    /// <summary>No own-account found for the client — refresh skipped; generation degrades gracefully.</summary>
    SkippedNoOwnAccount,

    /// <summary>No action needed (reserved for future use or no-op paths).</summary>
    NoActionNeeded
}

/// <summary>
/// Outcome of a single <see cref="IClientAestheticEnsureFreshService.EnsureAvailableBlockingAsync"/>
/// call (issue #67, BAPA-CG-01/02). Distinct from <see cref="EnsureFreshOutcome"/> — the caller
/// (<c>ContentGenerationJobProcessor</c>) DOES branch on this value to decide whether generation
/// proceeds or the job ends terminally <c>Failed</c>.
/// </summary>
public enum EnsureAvailableOutcome
{
    /// <summary>A usable analyzed aesthetic is available — generation may proceed.</summary>
    Ready,

    /// <summary>No own account is configured for the client — no wait was performed.</summary>
    NoOwnAccount,

    /// <summary>The bounded wait elapsed (<c>maxWait</c>) before a usable aesthetic appeared.</summary>
    TimedOut,

    /// <summary>The in-flight/enqueued analysis reached a terminal state without producing a usable aesthetic.</summary>
    AnalysisFailed
}

/// <summary>
/// Application port for non-blocking, per-client aesthetic-data freshness orchestration.
///
/// <para>
/// The implementation checks whether the most recent completed analysis for the client is within
/// the effective staleness threshold (<c>client.AestheticRefreshThresholdDays ?? 30</c> days),
/// debounces duplicate in-flight refreshes (OQ-8), resolves the client's own Instagram account,
/// and fire-and-forgets a new <c>ProfileAnalysis</c> job through the existing
/// <c>IProfileAnalysisJobQueue</c> channel. It NEVER blocks the generation path.
/// </para>
///
/// <para>
/// CONTRACT:
/// <list type="bullet">
/// <item>NEVER throws into the caller — all exceptions must be caught internally and logged.</item>
/// <item>NEVER awaits the analysis pipeline — the enqueued job runs in <c>ProfileAnalysisBackgroundWorker</c>.</item>
/// <item>Returns <see cref="EnsureFreshOutcome"/> for logging/telemetry only; callers MUST NOT
///     conditionally skip generation based on the return value.</item>
/// </list>
/// </para>
///
/// Refs: design §3.3, §4; CAG-CG-01–04, CAG-CG-10.
/// No Infrastructure types in signature.
/// </summary>
public interface IClientAestheticEnsureFreshService
{
    /// <summary>
    /// Fire-and-forget orchestration: check staleness, debounce in-flight refreshes, resolve own
    /// account, enqueue a new analysis job if needed. Never throws into the generation path.
    /// The caller wraps this in a try/catch and logs any escaped exception before continuing.
    /// </summary>
    /// <param name="orgId">The organization that owns the client.</param>
    /// <param name="clientId">The client whose aesthetic data staleness is being checked.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome for structured logging; callers MUST NOT branch on this.</returns>
    Task<EnsureFreshOutcome> EnsureFreshAsync(Guid orgId, Guid clientId, CancellationToken ct);

    /// <summary>
    /// Returns <c>true</c> when the most-recent <c>Analyzed</c> analysis for the client carries a
    /// usable aesthetic payload right now — the single predicate the processor uses to choose
    /// between the fire-and-forget branch (<see cref="EnsureFreshAsync"/>) and the blocking branch
    /// (<see cref="EnsureAvailableBlockingAsync"/>). Delegates to
    /// <c>IProfileAnalysisRepository.LatestAnalyzedHasAestheticAsync</c> (issue #67, Design Decision 3).
    /// </summary>
    Task<bool> LatestUsableAestheticPresentAsync(Guid orgId, Guid clientId, CancellationToken ct);

    /// <summary>
    /// Blocking orchestration for the first-time / no-usable-aesthetic case (issue #67,
    /// BAPA-CG-01/BAPA-CG-02, Design Decisions 1-2, 6).
    ///
    /// <para>
    /// Resolves the client's own account (none → <see cref="EnsureAvailableOutcome.NoOwnAccount"/>,
    /// no wait), ensures an analysis is enqueued or already in flight, invokes
    /// <paramref name="onWaitStarted"/> EXACTLY ONCE iff a real wait begins (the caller uses this to
    /// write the <c>AnalyzingProfile</c> job status), then polls
    /// <c>IProfileAnalysisRepository.LatestAnalyzedHasAestheticAsync</c> under a linked
    /// <see cref="CancellationTokenSource"/> bounded by <paramref name="maxWait"/>.
    /// </para>
    ///
    /// <para>
    /// CONTRACT:
    /// <list type="bullet">
    /// <item>Never throws for non-cancellation reasons — repo/probe exceptions inside the poll loop
    ///     are caught and mapped to continued polling or <see cref="EnsureAvailableOutcome.AnalysisFailed"/>.</item>
    /// <item>When ONLY the <paramref name="maxWait"/> ceiling fires, returns
    ///     <see cref="EnsureAvailableOutcome.TimedOut"/> — it does NOT throw.</item>
    /// <item>When the caller's <paramref name="ct"/> is cancelled (real per-job timeout / shutdown),
    ///     <see cref="OperationCanceledException"/> propagates so the caller's outer handler can
    ///     record the cancellation-specific terminal status.</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="orgId">The organization that owns the client.</param>
    /// <param name="clientId">The client whose aesthetic availability is being awaited.</param>
    /// <param name="maxWait">The bounded ceiling for the wait (config: <c>ContentGeneration:AestheticGating:MaxWaitSeconds</c>).</param>
    /// <param name="onWaitStarted">Invoked exactly once, iff a real wait begins (never when the aesthetic is
    /// already usable and never when there is no own account). Lets the job-aware caller write a status
    /// transition without the service touching job state itself.</param>
    /// <param name="ct">Cancellation token for the caller's real (per-job) cancellation, distinct from <paramref name="maxWait"/>.</param>
    Task<EnsureAvailableOutcome> EnsureAvailableBlockingAsync(
        Guid orgId,
        Guid clientId,
        TimeSpan maxWait,
        Func<CancellationToken, Task>? onWaitStarted,
        CancellationToken ct);
}
