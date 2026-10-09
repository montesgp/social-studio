using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Application.Features.Social.SingleAnalyzer.Analyze;

/// <summary>
/// Recovery policy for single-analyzer analyses that never reached a terminal status because
/// their background job was lost — the queue backing <c>ISingleAnalysisJobQueue</c> is an
/// in-memory Channel, so an enqueued job never survives a process restart. Reproduced in
/// production (logs/hive-api.log 2026-09-09): a "Requested" analysis whose preflight succeeded
/// and was enqueued, but no "PROCESSING job" ever appeared afterward for the rest of the day.
///
/// <para>
/// Without this sweep such a record blocks <see cref="SingleContentAnalyzer"/>'s org-scoped
/// duplicate gate (<c>CheckOrgDuplicateAsync</c>) forever — the org could never analyze that URL
/// again, with no path to retry. This is a defense-in-depth backstop for that specific orphan
/// case, NOT the primary mechanism for preventing duplicate Apify spend — that is the hard,
/// synchronous gate in <c>StartAsync</c>.
/// </para>
///
/// <para>
/// Pure application logic (no hosting/scheduling concerns): the background worker decides WHEN to
/// call this (on a periodic timer); this type decides WHICH analyses to fail.
/// </para>
/// </summary>
public static class SingleAnalysisJobWatchdog
{
    /// <summary>
    /// A non-terminal analysis untouched for longer than this is considered stalled. Single-analyzer
    /// runs (one scrape + one AI call) normally complete within ~60s per the frontend's own copy;
    /// this threshold is generous specifically because it only needs to catch a truly ABANDONED job
    /// (lost queue entry, dead worker) — not to race a legitimately slow-but-progressing run.
    /// </summary>
    public static readonly TimeSpan StuckThreshold = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Periodic sweep: fails every analysis stuck in a non-terminal status whose
    /// <c>UpdatedAtUtc</c> is older than <see cref="StuckThreshold"/> relative to
    /// <paramref name="nowUtc"/>. Any already-persisted <c>ContentSnapshotJson</c> is preserved so
    /// the user can retry the AI stage alone afterward instead of losing the Apify credits already
    /// spent. Returns the count failed.
    /// </summary>
    public static async Task<int> FailStalledAnalysesAsync(
        ISingleContentAnalysisRepository analyses,
        IUnitOfWork unitOfWork,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var candidates = await analyses.ListNonTerminalAsync(cancellationToken);
        var failed = 0;

        foreach (var analysis in candidates)
        {
            if (nowUtc - analysis.UpdatedAtUtc <= StuckThreshold)
            {
                continue;
            }

            var updated = analysis with
            {
                Status = SingleContentAnalysisStatus.Failed,
                FailureCode = SingleAnalyzerErrors.WorkflowStalled.Code,
                FailureMessageKey = SingleAnalyzerErrors.WorkflowStalled.MessageKey,
                UpdatedAtUtc = nowUtc,
                CompletedAtUtc = nowUtc,
            };

            await analyses.UpdateAsync(updated, cancellationToken);
            failed++;
        }

        if (failed > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return failed;
    }
}
