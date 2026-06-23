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
}
