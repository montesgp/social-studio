using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Domain.Social.ClientOwnAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Background;

/// <summary>
/// Implements <see cref="IClientAestheticEnsureFreshService"/>: checks whether the client's
/// aesthetic analysis is within the effective staleness threshold and, when stale or missing,
/// fire-and-forgets a new analysis job through the existing <see cref="IProfileAnalysisJobQueue"/>
/// channel. Debounces duplicate in-flight refreshes (OQ-8). Degrades safely when no own account
/// is found — generation is NEVER blocked or delayed.
///
/// <para>
/// Algorithm mirrors <c>AnalyzeProfileHandler</c>: create aggregate → persist → enqueue.
/// Dependencies: <see cref="IProfileAnalysisRepository"/> (freshness + debounce + persist),
/// <see cref="ISocialClientRepository"/> (per-client threshold), <see cref="IClientOwnAccountRepository"/>
/// (own account resolution), <see cref="IUnitOfWork"/>, <see cref="IClock"/>,
/// <see cref="IProfileAnalysisJobQueue"/>. All ports — no Infrastructure types.
/// </para>
///
/// Refs: design §4, §4.1; CAG-CG-01–04, CAG-CG-10.
/// No EF/Npgsql/Apify types — pure Application.
/// </summary>
public sealed class ClientAestheticEnsureFreshService : IClientAestheticEnsureFreshService
{
    private readonly IProfileAnalysisRepository _analysisRepository;
    private readonly ISocialClientRepository _clientRepository;
    private readonly IClientOwnAccountRepository _ownAccountRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IProfileAnalysisJobQueue _queue;
    private readonly ILogger<ClientAestheticEnsureFreshService> _logger;
    private readonly TimeSpan _pollInterval;

    public ClientAestheticEnsureFreshService(
        IProfileAnalysisRepository analysisRepository,
        ISocialClientRepository clientRepository,
        IClientOwnAccountRepository ownAccountRepository,
        IUnitOfWork unitOfWork,
        IClock clock,
        IProfileAnalysisJobQueue queue,
        ILogger<ClientAestheticEnsureFreshService>? logger = null,
        // issue #67 (Design Decision 2): optional so existing tests/DI registrations compile
        // unchanged. When null, defaults to the documented 2s default
        // (ContentGeneration:AestheticGating:PollIntervalSeconds).
        IOptions<ContentGenerationAestheticGatingOptions>? gatingOptions = null)
    {
        _analysisRepository = analysisRepository;
        _clientRepository = clientRepository;
        _ownAccountRepository = ownAccountRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _queue = queue;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ClientAestheticEnsureFreshService>.Instance;
        _pollInterval = TimeSpan.FromSeconds(
            gatingOptions?.Value.PollIntervalSeconds ?? ContentGenerationAestheticGatingOptions.DefaultPollIntervalSeconds);
    }

    /// <inheritdoc/>
    public async Task<EnsureFreshOutcome> EnsureFreshAsync(Guid orgId, Guid clientId, CancellationToken ct)
    {
        // ── 1. Resolve per-client staleness threshold ──────────────────────────
        var client = await _clientRepository.GetByIdAsync(new ClientId(clientId), orgId, ct);
        var thresholdDays = client?.AestheticRefreshThresholdDays ?? AestheticFreshness.DefaultStalenessThresholdDays;
        var effectiveThreshold = TimeSpan.FromDays(thresholdDays);

        // ── 2. Freshness check ─────────────────────────────────────────────────
        // For ClientAesthetic, "fresh" requires BOTH a recent analysis AND that it actually carries
        // the captured aesthetic payload. A recent analysis with no aesthetic (e.g. captured before the
        // enrichment shipped, or an actor that returned no media) must NOT block a refresh — otherwise
        // generation keeps reading an aesthetic-less profile forever. (Phase 2 CAG2 fix.)
        var latestCompletedAt = await _analysisRepository.GetLatestAnalyzedCompletedAtAsync(orgId, clientId, ct);
        if (latestCompletedAt.HasValue && (_clock.UtcNow - latestCompletedAt.Value) <= effectiveThreshold)
        {
            var hasAesthetic = await _analysisRepository.LatestAnalyzedHasAestheticAsync(orgId, clientId, ct);
            if (hasAesthetic)
            {
                // aesthetic-freshness-versioning: a recent analysis with SOME aesthetic payload can
                // still be stale in a way age alone can't see — captured by an older extractor
                // schema version (missing fields that only a newer extractor populates), or never
                // materially complete. AestheticFreshness.Evaluate decides; Incomplete is treated
                // the same as Fresh (no refresh) to avoid looping forever without new data.
                var freshnessSnapshot = await _analysisRepository.GetLatestAestheticFreshnessAsync(orgId, clientId, ct);
                var verdict = AestheticFreshness.Evaluate(freshnessSnapshot);
                if (verdict != AestheticFreshnessVerdict.Refresh)
                {
                    return EnsureFreshOutcome.Fresh;
                }

                _logger.LogInformation(
                    "ClientAestheticEnsureFresh: latest analysis for client {ClientId} (org {OrgId}) is within " +
                    "the freshness window but was captured with an older extractor schema version " +
                    "({StoredVersion}) than the current one ({CurrentVersion}) — refreshing so the newer " +
                    "extractor can populate any previously-missing fields.",
                    clientId, orgId, freshnessSnapshot.SchemaVersion ?? "(none)", AestheticFreshness.CurrentSchemaVersion);
            }
            else
            {
                _logger.LogInformation(
                    "ClientAestheticEnsureFresh: latest analysis for client {ClientId} (org {OrgId}) is within the " +
                    "freshness window but has NO captured aesthetic — refreshing so generation can use it.",
                    clientId, orgId);
            }
        }

        // ── 3. Debounce: skip if a non-terminal analysis is already in flight ─
        var hasInFlight = await _analysisRepository.HasNonTerminalAnalysisForClientAsync(orgId, clientId, ct);
        if (hasInFlight)
        {
            _logger.LogDebug(
                "ClientAestheticEnsureFresh: in-flight analysis already exists for client {ClientId} (org {OrgId}); skipping refresh.",
                clientId, orgId);
            return EnsureFreshOutcome.SkippedInFlight;
        }

        // ── 4. Resolve own account ─────────────────────────────────────────────
        var ownAccounts = await _ownAccountRepository.GetAllByClientAsync(
            new ClientId(clientId), orgId, ct);

        var primaryAccount = ownAccounts.FirstOrDefault(a => a.IsActive) ?? ownAccounts.FirstOrDefault();
        if (primaryAccount is null)
        {
            _logger.LogWarning(
                "ClientAestheticEnsureFresh: no own account found for client {ClientId} (org {OrgId}). Skipping refresh; generation degrades gracefully.",
                clientId, orgId);
            return EnsureFreshOutcome.SkippedNoOwnAccount;
        }

        // ── 5. Create analysis, persist, enqueue (mirrors AnalyzeProfileHandler) ─
        var target = InstagramProfileTargetNormalizer.Normalize(primaryAccount.Handle ?? primaryAccount.ProfileUrl);

        var analysis = ProfileAnalysis.CreateFromClient(
            orgId,
            clientId,
            primaryAccount.Id.Value,
            target,
            _clock.UtcNow);

        try
        {
            await _analysisRepository.AddAsync(analysis, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            await _queue.EnqueueAsync(new ProfileAnalysisJob(analysis.Id, orgId), ct);

            _logger.LogInformation(
                "ClientAestheticEnsureFresh: enqueued analysis {AnalysisId} for client {ClientId} (org {OrgId}), target {Target}.",
                analysis.Id, clientId, orgId, target);

            return EnsureFreshOutcome.RefreshEnqueued;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ClientAestheticEnsureFresh: failed to enqueue refresh for client {ClientId} (org {OrgId}). Generation proceeds with best-available data.",
                clientId, orgId);
            // Per CAG-CG-04: swallow and log — never propagate into the generation path.
            return EnsureFreshOutcome.RefreshEnqueued;
        }
    }

    /// <inheritdoc/>
    public Task<bool> LatestUsableAestheticPresentAsync(Guid orgId, Guid clientId, CancellationToken ct) =>
        _analysisRepository.LatestAnalyzedHasAestheticAsync(orgId, clientId, ct);

    /// <inheritdoc/>
    public async Task<EnsureAvailableOutcome> EnsureAvailableBlockingAsync(
        Guid orgId,
        Guid clientId,
        TimeSpan maxWait,
        Func<CancellationToken, Task>? onWaitStarted,
        CancellationToken ct)
    {
        // ── 1. Resolve own account — no wait, no enqueue when absent (BAPA-CG-02) ──────────────
        // Reliability review follow-up (issue #67): the interface doc promises this method never
        // throws for non-cancellation reasons. Own-account resolution and the in-flight probe
        // below both hit the repository BEFORE the poll loop's own try/catch begins, so a
        // transient repo error here must be caught explicitly too, or it escapes uncaught into
        // the processor and surfaces as a generic "unhandled:{Type}" job failure instead of the
        // actionable aesthetic-analysis-failed code. A real cancellation of the caller's ct still
        // propagates — only non-cancellation exceptions are mapped to AnalysisFailed.
        IReadOnlyList<ClientOwnAccount> ownAccounts;
        bool hasInFlight;
        try
        {
            ownAccounts = await _ownAccountRepository.GetAllByClientAsync(new ClientId(clientId), orgId, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ClientAestheticEnsureFresh: EnsureAvailableBlockingAsync — own-account resolution failed for client {ClientId} (org {OrgId}).",
                clientId, orgId);
            return EnsureAvailableOutcome.AnalysisFailed;
        }

        var primaryAccount = ownAccounts.FirstOrDefault(a => a.IsActive) ?? ownAccounts.FirstOrDefault();
        if (primaryAccount is null)
        {
            _logger.LogWarning(
                "ClientAestheticEnsureFresh: EnsureAvailableBlockingAsync — no own account found for client {ClientId} (org {OrgId}); failing immediately (BAPA-CG-02).",
                clientId, orgId);
            return EnsureAvailableOutcome.NoOwnAccount;
        }

        // ── 2. Ensure an analysis is in flight — reuse the enqueue path unless one already exists ──
        try
        {
            hasInFlight = await _analysisRepository.HasNonTerminalAnalysisForClientAsync(orgId, clientId, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ClientAestheticEnsureFresh: EnsureAvailableBlockingAsync — in-flight probe failed for client {ClientId} (org {OrgId}).",
                clientId, orgId);
            return EnsureAvailableOutcome.AnalysisFailed;
        }

        if (!hasInFlight)
        {
            var target = InstagramProfileTargetNormalizer.Normalize(primaryAccount.Handle ?? primaryAccount.ProfileUrl);
            var analysis = ProfileAnalysis.CreateFromClient(orgId, clientId, primaryAccount.Id.Value, target, _clock.UtcNow);

            try
            {
                await _analysisRepository.AddAsync(analysis, ct);
                await _unitOfWork.SaveChangesAsync(ct);
                await _queue.EnqueueAsync(new ProfileAnalysisJob(analysis.Id, orgId), ct);

                _logger.LogInformation(
                    "ClientAestheticEnsureFresh: EnsureAvailableBlockingAsync — enqueued analysis {AnalysisId} for client {ClientId} (org {OrgId}), target {Target}.",
                    analysis.Id, clientId, orgId, target);
            }
            catch (Exception ex)
            {
                // Enqueue is still fire-and-forget (CAG-CG-04) even on the blocking path — swallow
                // and fall through to the poll loop below. The poll's own terminal-state check
                // (no aesthetic + nothing in flight) resolves this to AnalysisFailed if nothing
                // ever appears, or TimedOut if maxWait elapses first.
                _logger.LogWarning(ex,
                    "ClientAestheticEnsureFresh: EnsureAvailableBlockingAsync — failed to enqueue analysis for client {ClientId} (org {OrgId}).",
                    clientId, orgId);
            }
        }

        // ── 3. A real wait begins now — notify the caller exactly once (Design Decision 1) ─────
        if (onWaitStarted is not null)
        {
            await onWaitStarted(ct);
        }

        // ── 4. Poll under a linked CTS bounded by maxWait (Design Decision 6) ──────────────────
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(maxWait);

        try
        {
            while (true)
            {
                try
                {
                    var hasAesthetic = await _analysisRepository.LatestAnalyzedHasAestheticAsync(orgId, clientId, linkedCts.Token);
                    if (hasAesthetic)
                    {
                        return EnsureAvailableOutcome.Ready;
                    }

                    var stillInFlight = await _analysisRepository.HasNonTerminalAnalysisForClientAsync(orgId, clientId, linkedCts.Token);
                    if (!stillInFlight)
                    {
                        // The analysis reached a terminal state without producing a usable aesthetic.
                        _logger.LogWarning(
                            "ClientAestheticEnsureFresh: EnsureAvailableBlockingAsync — analysis for client {ClientId} (org {OrgId}) reached a terminal state with no usable aesthetic.",
                            clientId, orgId);
                        return EnsureAvailableOutcome.AnalysisFailed;
                    }
                }
                catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
                {
                    // Re-thrown below by the outer catch — distinguishes maxWait vs. job ct.
                    throw;
                }
                catch (Exception ex)
                {
                    // Repo/probe exceptions inside the loop are never allowed to propagate
                    // uncaught (Design Decision 6) — log and keep polling up to the bound.
                    _logger.LogWarning(ex,
                        "ClientAestheticEnsureFresh: EnsureAvailableBlockingAsync — probe failed for client {ClientId} (org {OrgId}); continuing to poll.",
                        clientId, orgId);
                }

                await Task.Delay(_pollInterval, linkedCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested)
            {
                // Real per-job cancellation / shutdown — propagate so the caller's outer handler
                // records the cancellation-specific terminal status.
                throw;
            }

            // Only the maxWait ceiling fired — never throw for this case (Design Decision 6).
            _logger.LogWarning(
                "ClientAestheticEnsureFresh: EnsureAvailableBlockingAsync — timed out waiting for client {ClientId} (org {OrgId}) after {MaxWait}.",
                clientId, orgId, maxWait);
            return EnsureAvailableOutcome.TimedOut;
        }
    }
}
