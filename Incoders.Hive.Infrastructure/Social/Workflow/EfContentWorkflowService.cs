using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Ideas;
using Incoders.Hive.Application.Features.Social.Workflow;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Infrastructure.Social.Workflow;

/// <summary>
/// Replaces <c>InMemoryContentWorkflowService</c> (Phase 5.9 facade rewrite, design decision #2):
/// discovery orchestration is UNCHANGED — same start+poll model, same progress-ramp math — but session
/// and candidate state now live in <c>content_workflow_sessions</c>/<c>_candidates</c> behind
/// <see cref="IContentWorkflowSessionRepository"/> instead of a <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>.
/// The mock generation paths the prior service exposed (<c>GetPreviewAsync</c>, <c>RunActionAsync</c>,
/// <c>GeneratePackageAsync</c>) are DELETED per spec, not ported: the real per-candidate composition +
/// enqueue flow (<see cref="EnqueueContentWorkflowCandidatesCommand"/> et al.) replaces them.
/// </summary>
public sealed class EfContentWorkflowService : IContentWorkflowService
{
    private const int DefaultResultCount = 72;
    private const string AiIdeasOriginMode = "ai-ideas";
    private const int MaxInProgressPercentage = 95;
    private const int HistoryDefaultLimit = 12;
    private const double ProgressPhaseDurationSeconds = 2.2;

    private readonly IContentWorkflowSessionRepository _repository;
    private readonly IClock _clock;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<EfContentWorkflowService> _logger;

    public EfContentWorkflowService(
        IContentWorkflowSessionRepository repository,
        IClock clock,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime applicationLifetime,
        ILogger<EfContentWorkflowService> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(applicationLifetime);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _clock = clock;
        _scopeFactory = scopeFactory;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    public async Task<Result<ContentWorkflowSessionResponse>> StartAsync(
        Guid organizationId,
        ContentWorkflowStartInput input,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty)
        {
            return Result.Failure<ContentWorkflowSessionResponse>(ContentWorkflowErrors.OrganizationRequired);
        }

        if (input.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(input.ClientName))
        {
            return Result.Failure<ContentWorkflowSessionResponse>(ContentWorkflowErrors.ClientRequired);
        }

        var searchCriterion = ContentWorkflowCatalog.NormalizeSearchCriterion(input.SearchCriterion);
        if (searchCriterion.Length == 0)
        {
            return Result.Failure<ContentWorkflowSessionResponse>(ContentWorkflowErrors.SearchCriterionInvalid);
        }

        var strategyMode = ContentWorkflowCatalog.NormalizeStrategyMode(input.StrategyMode);
        if (strategyMode.Length == 0)
        {
            return Result.Failure<ContentWorkflowSessionResponse>(ContentWorkflowErrors.StrategyModeInvalid);
        }

        var originMode = ContentWorkflowCatalog.NormalizeOriginMode(input.OriginMode);
        if (originMode.Length == 0)
        {
            return Result.Failure<ContentWorkflowSessionResponse>(ContentWorkflowErrors.OriginModeInvalid);
        }

        var normalizedNetworks = ContentWorkflowCatalog.NormalizeNetworkSources(input.NetworkSources);

        if (normalizedNetworks.Count == 0 && originMode != AiIdeasOriginMode)
        {
            return Result.Failure<ContentWorkflowSessionResponse>(ContentWorkflowErrors.NetworkSourcesRequired);
        }

        var nowUtc = _clock.UtcNow;
        var sessionId = Guid.NewGuid();
        var clientName = input.ClientName.Trim();

        var config = new SessionConfig(
            searchCriterion,
            strategyMode,
            normalizedNetworks,
            input.ForceRefresh,
            originMode,
            DiscoveryPhase.InProgress,
            string.Empty,
            null);

        await _repository.InsertAsync(
            WorkflowSession.Create(sessionId),
            organizationId,
            input.ClientId,
            clientName,
            StatusFor(DiscoveryPhase.InProgress),
            Serialize(config),
            cancellationToken);

        // Fire and forget on purpose (unchanged from InMemoryContentWorkflowService): a real scrape does
        // not fit inside one HTTP request. The background task builds its OWN scope so it survives the
        // request's scoped DbContext being disposed.
        _ = originMode == AiIdeasOriginMode
            ? Task.Run(() => RunIdeaDrawAsync(sessionId, organizationId, input.ClientId, config), CancellationToken.None)
            : Task.Run(() => RunDiscoveryAsync(sessionId, organizationId, input.ClientId, config), CancellationToken.None);

        var snapshot = ComputeProgressSnapshot(config.Phase, nowUtc, nowUtc, config.NetworkSources, 0);

        return Result.Success(new ContentWorkflowSessionResponse(
            sessionId,
            input.ClientId,
            clientName,
            searchCriterion,
            strategyMode,
            normalizedNetworks,
            snapshot.WorkflowStatus,
            snapshot.ProgressPercentage,
            snapshot.Message,
            0,
            0,
            nowUtc,
            nowUtc));
    }

    private async Task RunDiscoveryAsync(Guid sessionId, Guid organizationId, Guid clientId, SessionConfig config)
    {
        var ct = _applicationLifetime.ApplicationStopping;

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IContentWorkflowSessionRepository>();

        try
        {
            var discovery = scope.ServiceProvider.GetRequiredService<IContentDiscoveryService>();

            var plan = await discovery.DiscoverAsync(
                new ContentDiscoveryRequest(
                    organizationId,
                    clientId,
                    config.NetworkSources,
                    DefaultResultCount,
                    config.SearchCriterion,
                    config.ForceRefresh),
                ct);

            var candidates = ProjectCandidates(plan, config.SearchCriterion, config.StrategyMode);
            await repository.AddCandidatesAsync(sessionId, candidates, ct);

            var terminalPhase = ResolveTerminalPhase(plan);
            var resultOrigin = plan.Origin == DiscoveryOrigin.Bank ? "bank" : "fresh";
            var updated = config with { Phase = terminalPhase, ResultOrigin = resultOrigin, BankObservedAtUtc = plan.BankObservedAtUtc };

            await repository.UpdateSessionStateAsync(sessionId, StatusFor(terminalPhase), Serialize(updated), ct);

            _logger.LogInformation(
                "Content workflow discovery finished for {WorkflowId} with phase {Phase} and {CandidateCount} candidate(s).",
                sessionId, terminalPhase, candidates.Count);
        }
        catch (Exception ex)
        {
            var updated = config with { Phase = DiscoveryPhase.Failed };
            await repository.UpdateSessionStateAsync(sessionId, StatusFor(DiscoveryPhase.Failed), Serialize(updated), ct);

            _logger.LogError(
                ex,
                "Content workflow discovery failed for {WorkflowId} (client {ClientId}, criterion {SearchCriterion}).",
                sessionId, clientId, config.SearchCriterion);
        }
    }

    private async Task RunIdeaDrawAsync(Guid sessionId, Guid organizationId, Guid clientId, SessionConfig config)
    {
        var ct = _applicationLifetime.ApplicationStopping;

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IContentWorkflowSessionRepository>();

        try
        {
            var ideas = scope.ServiceProvider.GetRequiredService<ContentIdeaChain>();

            var plan = await ideas.DrawAsync(
                new ContentIdeaRequest(
                    organizationId,
                    clientId,
                    DefaultResultCount,
                    OriginFilter: IdeaOrigin.Invented,
                    VariationSeed: sessionId.ToString("N")),
                ct);

            var candidates = ProjectIdeas(plan, config.SearchCriterion, config.StrategyMode);
            await repository.AddCandidatesAsync(sessionId, candidates, ct);

            var terminalPhase = ResolveIdeaTerminalPhase(plan);
            var updated = config with { Phase = terminalPhase, ResultOrigin = "ai-ideas", BankObservedAtUtc = null };

            await repository.UpdateSessionStateAsync(sessionId, StatusFor(terminalPhase), Serialize(updated), ct);

            _logger.LogInformation(
                "Content workflow idea draw finished for {WorkflowId} with phase {Phase}, {IdeaCount} idea(s) and shortfall {Shortfall}.",
                sessionId, terminalPhase, candidates.Count, plan.Shortfall);
        }
        catch (Exception ex)
        {
            var updated = config with { Phase = DiscoveryPhase.Failed };
            await repository.UpdateSessionStateAsync(sessionId, StatusFor(DiscoveryPhase.Failed), Serialize(updated), ct);

            _logger.LogError(ex, "Content workflow idea draw failed for {WorkflowId} (client {ClientId}).", sessionId, clientId);
        }
    }

    private static DiscoveryPhase ResolveIdeaTerminalPhase(ContentIdeaPlan plan) =>
        plan.Ideas.Count > 0 ? DiscoveryPhase.Completed
        : plan.FailedSourceKeys.Count > 0 ? DiscoveryPhase.Failed
        : DiscoveryPhase.Completed;

    private static IReadOnlyList<WorkflowDiscoveredCandidate> ProjectIdeas(
        ContentIdeaPlan plan, string searchCriterion, string strategyMode) =>
        plan.Ideas
            .Select(idea =>
            {
                var id = Guid.NewGuid();
                var payload = new CandidatePayload(
                    idea.Title,
                    idea.SourceKey,
                    searchCriterion,
                    strategyMode,
                    idea.Reference ?? string.Empty,
                    string.Empty,
                    idea.Summary ?? string.Empty,
                    0,
                    0,
                    0);

                return new WorkflowDiscoveredCandidate(id, id.ToString("N"), Serialize(payload));
            })
            .ToList();

    private static DiscoveryPhase ResolveTerminalPhase(ContentDiscoveryPlan plan)
    {
        if (plan.Candidates.Count > 0)
        {
            return DiscoveryPhase.Completed;
        }

        if (plan.FailedSourceKeys.Count > 0)
        {
            return DiscoveryPhase.Failed;
        }

        if (plan.UnconfiguredSourceKeys.Count > 0)
        {
            return DiscoveryPhase.Unconfigured;
        }

        return DiscoveryPhase.Completed;
    }

    private static IReadOnlyList<WorkflowDiscoveredCandidate> ProjectCandidates(
        ContentDiscoveryPlan plan, string searchCriterion, string strategyMode) =>
        plan.Candidates
            .Select(item =>
            {
                var id = Guid.NewGuid();
                var payload = new CandidatePayload(
                    item.Title,
                    item.NetworkSource,
                    searchCriterion,
                    strategyMode,
                    item.ContentUrl,
                    item.ThumbnailUrl ?? string.Empty,
                    item.Summary,
                    ClampToInt(item.Views),
                    ClampToInt(item.Likes),
                    ClampToInt(item.Comments));

                return new WorkflowDiscoveredCandidate(id, id.ToString("N"), Serialize(payload));
            })
            .ToList();

    private static int ClampToInt(long? value) =>
        value is null or <= 0 ? 0 : value >= int.MaxValue ? int.MaxValue : (int)value;

    public async Task<Result<ContentWorkflowStatusResponse>> GetStatusAsync(
        Guid organizationId,
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        var detailResult = await GetOwnedDetailAsync(organizationId, workflowId, cancellationToken);
        if (detailResult.IsFailure)
        {
            return Result.Failure<ContentWorkflowStatusResponse>(detailResult.Error);
        }

        var detail = detailResult.Value;
        var config = Deserialize(detail.ConfigJson);
        var nowUtc = _clock.UtcNow;
        var snapshot = ComputeProgressSnapshot(config.Phase, detail.CreatedAtUtc, nowUtc, config.NetworkSources, detail.Candidates.Count);

        return Result.Success(new ContentWorkflowStatusResponse(
            detail.Id,
            snapshot.WorkflowStatus,
            snapshot.ProgressPercentage,
            snapshot.Message,
            snapshot.Steps,
            detail.UpdatedAtUtc,
            detail.Candidates.Count,
            config.ResultOrigin,
            config.BankObservedAtUtc));
    }

    public async Task<Result<PageResult<ContentWorkflowResultResponse>>> GetResultsAsync(
        Guid organizationId,
        Guid workflowId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page <= 0 || pageSize <= 0)
        {
            return Result.Failure<PageResult<ContentWorkflowResultResponse>>(ContentWorkflowErrors.SelectionRequired);
        }

        var detailResult = await GetOwnedDetailAsync(organizationId, workflowId, cancellationToken);
        if (detailResult.IsFailure)
        {
            return Result.Failure<PageResult<ContentWorkflowResultResponse>>(detailResult.Error);
        }

        var ordered = detailResult.Value.Candidates
            .Select(ToResultResponse)
            .OrderByDescending(x => x.Views)
            .ThenBy(x => x.Title)
            .ToList();

        var pageItems = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Result.Success(new PageResult<ContentWorkflowResultResponse>(pageItems, page, pageSize, ordered.Count));
    }

    public async Task<Result<ContentWorkflowSelectionResponse>> SetSelectionAsync(
        Guid organizationId,
        Guid workflowId,
        IReadOnlyList<Guid> resultIds,
        CancellationToken cancellationToken = default)
    {
        if (resultIds.Count == 0)
        {
            return Result.Failure<ContentWorkflowSelectionResponse>(ContentWorkflowErrors.SelectionRequired);
        }

        var detailResult = await GetOwnedDetailAsync(organizationId, workflowId, cancellationToken);
        if (detailResult.IsFailure)
        {
            return Result.Failure<ContentWorkflowSelectionResponse>(detailResult.Error);
        }

        var knownIds = detailResult.Value.Candidates.Select(x => x.Id).ToHashSet();
        var normalizedSelection = resultIds.Distinct().ToList();
        if (normalizedSelection.Any(id => !knownIds.Contains(id)))
        {
            return Result.Failure<ContentWorkflowSelectionResponse>(ContentWorkflowErrors.ResultNotFound);
        }

        await _repository.ReplaceSelectionAsync(workflowId, normalizedSelection, cancellationToken);
        var nowUtc = _clock.UtcNow;

        return Result.Success(new ContentWorkflowSelectionResponse(
            workflowId,
            normalizedSelection.Count,
            normalizedSelection.OrderBy(x => x).ToList(),
            nowUtc));
    }

    public async Task<Result<ContentWorkflowHistoryResponse>> GetHistoryAsync(
        Guid organizationId,
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        var detailResult = await GetOwnedDetailAsync(organizationId, workflowId, cancellationToken);
        if (detailResult.IsFailure)
        {
            return Result.Failure<ContentWorkflowHistoryResponse>(detailResult.Error);
        }

        var summary = ToHistoryItem(detailResult.Value);
        // Discovery persists every discovered candidate as `Discovered` (see AddCandidatesAsync) so the
        // operator can select/compose them — that is not the same as being submitted to the real
        // analysis-then-batch pipeline. Only candidates that actually left `Discovered` (i.e. were
        // composed and enqueued — see EnqueueContentWorkflowCandidatesCommandHandler) belong on this
        // read surface; otherwise every un-composed candidate would misleadingly show as "Queued".
        var candidates = detailResult.Value.Candidates
            .Where(c => c.Status != WorkflowCandidateStatus.Discovered.ToString())
            .Select(ToCandidateStatus)
            .ToList();

        return Result.Success(new ContentWorkflowHistoryResponse(summary, candidates));
    }

    public async Task<Result<IReadOnlyList<ContentWorkflowHistoryItemResponse>>> ListHistoryAsync(
        Guid organizationId,
        int limit,
        Guid? clientId = null,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty)
        {
            return Result.Failure<IReadOnlyList<ContentWorkflowHistoryItemResponse>>(ContentWorkflowErrors.OrganizationRequired);
        }

        var normalizedLimit = limit <= 0 ? HistoryDefaultLimit : Math.Min(limit, 50);

        var summaries = await _repository.ListAsync(organizationId, clientId, normalizedLimit, cancellationToken);

        var items = summaries.Select(ToHistoryItem).ToList();
        return Result.Success<IReadOnlyList<ContentWorkflowHistoryItemResponse>>(items);
    }

    private async Task<Result<WorkflowSessionDetail>> GetOwnedDetailAsync(
        Guid organizationId, Guid workflowId, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty)
        {
            return Result.Failure<WorkflowSessionDetail>(ContentWorkflowErrors.OrganizationRequired);
        }

        var detail = await _repository.GetDetailAsync(workflowId, organizationId, cancellationToken);
        return detail is null
            ? Result.Failure<WorkflowSessionDetail>(ContentWorkflowErrors.NotFound)
            : Result.Success(detail);
    }

    private static ContentWorkflowResultResponse ToResultResponse(WorkflowCandidateDetail candidate)
    {
        var payload = DeserializePayload(candidate.PayloadJson);
        return new ContentWorkflowResultResponse(
            candidate.Id,
            payload.Title,
            payload.NetworkSource,
            payload.SearchCriterion,
            payload.StrategyMode,
            payload.VideoUrl,
            payload.ThumbnailUrl,
            payload.Summary,
            payload.Views,
            payload.Likes,
            payload.Comments,
            candidate.IsSelected);
    }

    private static ContentWorkflowCandidateStatusResponse ToCandidateStatus(WorkflowCandidateDetail candidate)
    {
        var payload = DeserializePayload(candidate.PayloadJson);
        return new ContentWorkflowCandidateStatusResponse(candidate.Id, payload.Title, candidate.Status, candidate.FailureCode);
    }

    private static ContentWorkflowHistoryItemResponse ToHistoryItem(WorkflowSessionDetail detail)
    {
        var config = Deserialize(detail.ConfigJson);
        return new ContentWorkflowHistoryItemResponse(
            detail.Id,
            detail.ClientId,
            detail.ClientName,
            config.SearchCriterion,
            config.StrategyMode,
            config.NetworkSources,
            detail.Status,
            detail.Candidates.Count,
            detail.Candidates.Count(c => c.IsSelected),
            detail.CreatedAtUtc,
            detail.UpdatedAtUtc);
    }

    private static ContentWorkflowHistoryItemResponse ToHistoryItem(WorkflowSessionSummary summary)
    {
        var config = Deserialize(summary.ConfigJson);
        return new ContentWorkflowHistoryItemResponse(
            summary.Id,
            summary.ClientId,
            summary.ClientName,
            config.SearchCriterion,
            config.StrategyMode,
            config.NetworkSources,
            summary.Status,
            summary.TotalResults,
            summary.SelectedCount,
            summary.CreatedAtUtc,
            summary.UpdatedAtUtc);
    }

    private static ProgressSnapshot ComputeProgressSnapshot(
        DiscoveryPhase phase, DateTime createdAtUtc, DateTime nowUtc, IReadOnlyList<string> networkSources, int resultCount)
    {
        var phases = BuildProgressPhases(networkSources);
        var totalDuration = Math.Max(ProgressPhaseDurationSeconds, phases.Count * ProgressPhaseDurationSeconds);
        var elapsedSeconds = Math.Max(0, (nowUtc - createdAtUtc).TotalSeconds);
        var normalizedRatio = Math.Min(1d, elapsedSeconds / totalDuration);
        var rampPercentage = (int)Math.Round(normalizedRatio * 100, MidpointRounding.AwayFromZero);

        var isRunning = phase == DiscoveryPhase.InProgress;
        var progress = isRunning ? Math.Min(MaxInProgressPercentage, rampPercentage) : 100;

        var activeIndex = isRunning
            ? Math.Clamp((int)Math.Floor(elapsedSeconds / ProgressPhaseDurationSeconds), 0, phases.Count - 1)
            : phases.Count - 1;

        var terminalStepStatus = phase switch
        {
            DiscoveryPhase.Failed => "failed",
            DiscoveryPhase.Unconfigured => "blocked",
            _ => "completed",
        };

        var steps = new List<ContentWorkflowStepResponse>(phases.Count);
        for (var i = 0; i < phases.Count; i++)
        {
            var stepProgress = 0;
            var stepStatus = "pending";

            if (!isRunning)
            {
                stepStatus = terminalStepStatus;
                stepProgress = 100;
            }
            else if (i < activeIndex)
            {
                stepStatus = "completed";
                stepProgress = 100;
            }
            else if (i == activeIndex)
            {
                stepStatus = "in_progress";
                var phaseElapsed = elapsedSeconds - (i * ProgressPhaseDurationSeconds);
                var ratio = Math.Clamp(phaseElapsed / ProgressPhaseDurationSeconds, 0d, 1d);
                stepProgress = (int)Math.Round(ratio * 100, MidpointRounding.AwayFromZero);
            }

            steps.Add(new ContentWorkflowStepResponse(phases[i].Key, phases[i].Label, stepStatus, phases[i].Message, stepProgress));
        }

        var message = phase switch
        {
            DiscoveryPhase.InProgress => phases[activeIndex].Message,
            DiscoveryPhase.Failed => "Content discovery failed. The provider could not be reached or rejected the run.",
            DiscoveryPhase.Unconfigured =>
                "Content discovery is not configured for the selected network. Map or route a search actor under Settings / Integrations.",
            _ when resultCount == 0 => "Content discovery finished and found no candidates.",
            _ => "Content retrieval and analysis completed.",
        };

        return new ProgressSnapshot(progress, StatusFor(phase), message, steps);
    }

    private static string StatusFor(DiscoveryPhase phase) => phase switch
    {
        DiscoveryPhase.InProgress => "in_progress",
        DiscoveryPhase.Failed => "failed",
        DiscoveryPhase.Unconfigured => "unconfigured",
        _ => "completed",
    };

    private static List<ProgressPhase> BuildProgressPhases(IReadOnlyList<string> networkSources)
    {
        var phases = new List<ProgressPhase>();
        var normalizedNetworks = networkSources.Count == 0 ? new[] { "tiktok" } : networkSources.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var network in normalizedNetworks)
        {
            phases.Add(new ProgressPhase($"retrieve-{network}", $"Retrieve from {network}", ProviderProgressMessage(network)));
        }

        phases.Add(new ProgressPhase(
            "analyze", "AI analysis and script drafting", "Generating video analysis and script drafts for preview tabs..."));
        phases.Add(new ProgressPhase(
            "finalize", "Finalize cards and pagination", "Preparing paginated result cards and selection state..."));

        return phases;
    }

    private static string ProviderProgressMessage(string networkSource) => networkSource switch
    {
        "tiktok" => "Retrieving videos from TikTok API...",
        "instagram" => "Retrieving videos from Instagram via Apify adapter...",
        "youtube" => "Retrieving trend candidates through Data365 adapter...",
        _ => $"Retrieving content from {networkSource} provider adapter...",
    };

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    private static SessionConfig Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return SessionConfig.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<SessionConfig>(json) ?? SessionConfig.Empty;
        }
        catch (JsonException)
        {
            return SessionConfig.Empty;
        }
    }

    private static CandidatePayload DeserializePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CandidatePayload.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<CandidatePayload>(json) ?? CandidatePayload.Empty;
        }
        catch (JsonException)
        {
            return CandidatePayload.Empty;
        }
    }

    private enum DiscoveryPhase
    {
        InProgress,
        Completed,
        Failed,
        Unconfigured,
    }

    /// <summary>Session-level discovery state, serialized into <c>content_workflow_sessions.config_json</c>.
    /// Opaque to Application/Domain — this shape is an Infrastructure concern only.</summary>
    private sealed record SessionConfig(
        string SearchCriterion,
        string StrategyMode,
        IReadOnlyList<string> NetworkSources,
        bool ForceRefresh,
        string OriginMode,
        DiscoveryPhase Phase,
        string ResultOrigin,
        DateTimeOffset? BankObservedAtUtc)
    {
        public static SessionConfig Empty { get; } =
            new(string.Empty, string.Empty, [], false, string.Empty, DiscoveryPhase.InProgress, string.Empty, null);
    }

    /// <summary>One candidate's discovery payload, serialized into
    /// <c>content_workflow_candidates.payload_json</c>.</summary>
    private sealed record CandidatePayload(
        string Title,
        string NetworkSource,
        string SearchCriterion,
        string StrategyMode,
        string VideoUrl,
        string ThumbnailUrl,
        string Summary,
        int Views,
        int Likes,
        int Comments)
    {
        public static CandidatePayload Empty { get; } =
            new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, 0, 0);
    }

    private sealed record ProgressPhase(string Key, string Label, string Message);

    private sealed record ProgressSnapshot(
        int ProgressPercentage, string WorkflowStatus, string Message, IReadOnlyList<ContentWorkflowStepResponse> Steps);
}
