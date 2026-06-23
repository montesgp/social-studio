using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Microsoft.Extensions.Logging;

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

    public ClientAestheticEnsureFreshService(
        IProfileAnalysisRepository analysisRepository,
        ISocialClientRepository clientRepository,
        IClientOwnAccountRepository ownAccountRepository,
        IUnitOfWork unitOfWork,
        IClock clock,
        IProfileAnalysisJobQueue queue,
        ILogger<ClientAestheticEnsureFreshService>? logger = null)
    {
        _analysisRepository = analysisRepository;
        _clientRepository = clientRepository;
        _ownAccountRepository = ownAccountRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _queue = queue;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ClientAestheticEnsureFreshService>.Instance;
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
                return EnsureFreshOutcome.Fresh;
            }

            _logger.LogInformation(
                "ClientAestheticEnsureFresh: latest analysis for client {ClientId} (org {OrgId}) is within the " +
                "freshness window but has NO captured aesthetic — refreshing so generation can use it.",
                clientId, orgId);
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
}
