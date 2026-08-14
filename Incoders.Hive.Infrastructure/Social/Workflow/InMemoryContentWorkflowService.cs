using System.Collections.Concurrent;
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
/// Start + poll, not request/response. A real scrape does not fit inside one HTTP request: production
/// runs were aborted by the browser or a proxy at ~30 s ("Niche search CANCELLED BY THE CALLER after
/// 29965 ms"), and every abort left an Apify actor running and billing. <see cref="StartAsync"/>
/// therefore validates, opens the session and returns; discovery runs on a background task whose only
/// cancellation source is application shutdown, and the operator polls
/// <see cref="GetStatusAsync"/> until the session reaches a terminal state.
///
/// Sessions live in a <see cref="ConcurrentDictionary{TKey,TValue}"/> and are lost when the process
/// restarts. That is a known, accepted limitation of this in-memory implementation: a poll for an id
/// this process never saw answers <see cref="ContentWorkflowErrors.NotFound"/>, it does not throw.
/// </summary>
public sealed class InMemoryContentWorkflowService : IContentWorkflowService
{
    private const int DefaultResultCount = 72;

    /// <summary>
    /// The canonical origin mode that draws invented topics instead of reaching any provider. Compared
    /// against the NORMALIZED value, never against raw operator input.
    /// </summary>
    private const string AiIdeasOriginMode = "ai-ideas";

    /// <summary>
    /// While discovery is still running the time-based ramp is capped below 100 so the bar can never
    /// claim completion the session has not reached. Only a terminal phase reports 100.
    /// </summary>
    private const int MaxInProgressPercentage = 95;

    /// <summary>
    /// Formats built for each selected proposal until the screen exposes the composition cart.
    /// Surface-scoped keys on purpose: the bare "carousel" alias is rejected on write paths.
    /// </summary>
    private static readonly string[] DefaultProposalFormats = ["reel-script", "feed-carousel"];
    private const int HistoryDefaultLimit = 12;
    private const double ProgressPhaseDurationSeconds = 2.2;

    private readonly IClock _clock;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<InMemoryContentWorkflowService> _logger;
    private readonly ConcurrentDictionary<Guid, WorkflowSessionState> _sessions = new();

    /// <param name="scopeFactory">
    /// Discovery is a SCOPED service (it reaches EF and the scraping providers) while this service is
    /// a SINGLETON, because the workflow sessions it holds must outlive a request. Resolving discovery
    /// through a scope is therefore mandatory rather than stylistic — and the background task creates
    /// its OWN scope, so no scoped service is ever captured across an await.
    /// </param>
    /// <param name="applicationLifetime">
    /// Supplies the only token the background discovery observes. The request's own token is exactly
    /// what the browser or proxy aborts, so wiring it into work that must outlive the request is the
    /// defect this service was built around.
    /// </param>
    public InMemoryContentWorkflowService(
        IClock clock,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime applicationLifetime,
        ILogger<InMemoryContentWorkflowService> logger)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(applicationLifetime);
        ArgumentNullException.ThrowIfNull(logger);

        _clock = clock;
        _scopeFactory = scopeFactory;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    /// <summary>
    /// Validates, opens the session in <c>in_progress</c> and returns immediately. The scrape happens
    /// on a background task; <paramref name="cancellationToken"/> guards only this validation, never
    /// the discovery it schedules.
    /// </summary>
    public Task<Result<ContentWorkflowSessionResponse>> StartAsync(
        Guid organizationId,
        ContentWorkflowStartInput input,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty)
        {
            return Task.FromResult(Result.Failure<ContentWorkflowSessionResponse>(ContentWorkflowErrors.OrganizationRequired));
        }

        if (input.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(input.ClientName))
        {
            return Task.FromResult(Result.Failure<ContentWorkflowSessionResponse>(ContentWorkflowErrors.ClientRequired));
        }

        var searchCriterion = ContentWorkflowCatalog.NormalizeSearchCriterion(input.SearchCriterion);
        if (searchCriterion.Length == 0)
        {
            return Task.FromResult(Result.Failure<ContentWorkflowSessionResponse>(ContentWorkflowErrors.SearchCriterionInvalid));
        }

        var strategyMode = ContentWorkflowCatalog.NormalizeStrategyMode(input.StrategyMode);
        if (strategyMode.Length == 0)
        {
            return Task.FromResult(Result.Failure<ContentWorkflowSessionResponse>(ContentWorkflowErrors.StrategyModeInvalid));
        }

        // Rejected rather than defaulted: silently reading an unrecognised mode as "discovery" would let
        // a typo start the billed scrape the operator was explicitly avoiding.
        var originMode = ContentWorkflowCatalog.NormalizeOriginMode(input.OriginMode);
        if (originMode.Length == 0)
        {
            return Task.FromResult(Result.Failure<ContentWorkflowSessionResponse>(ContentWorkflowErrors.OriginModeInvalid));
        }

        var normalizedNetworks = ContentWorkflowCatalog.NormalizeNetworkSources(input.NetworkSources);

        // A network is where we SCRAPE. The ai-ideas mode scrapes nowhere, so demanding one would be a
        // requirement the run never honours — the form would be lying to the operator.
        if (normalizedNetworks.Count == 0 && originMode != AiIdeasOriginMode)
        {
            return Task.FromResult(Result.Failure<ContentWorkflowSessionResponse>(ContentWorkflowErrors.NetworkSourcesRequired));
        }

        var nowUtc = _clock.UtcNow;
        var workflowId = Guid.NewGuid();

        var session = new WorkflowSessionState(
            workflowId,
            organizationId,
            input.ClientId,
            input.ClientName.Trim(),
            searchCriterion,
            strategyMode,
            normalizedNetworks,
            input.ForceRefresh,
            nowUtc,
            nowUtc,
            "in_progress",
            [],
            new HashSet<Guid>(),
            new List<ContentWorkflowActionResponse>(),
            originMode);

        _sessions[workflowId] = session;

        // Fire and forget ON PURPOSE. The scrape takes minutes; awaiting it here is what made the
        // browser or proxy abort the request at ~30 s and orphan a billing Apify run. The task owns
        // its own scope and its own cancellation source, so nothing here has to be awaited or
        // captured.
        //
        // The ai-ideas branch is far quicker (one model call, no provider), but it runs on the same
        // start+poll machinery on purpose: the operator keeps ONE way of watching a run, and a slow
        // model does not get to abort the request either.
        _ = originMode == AiIdeasOriginMode
            ? Task.Run(() => RunIdeaDrawAsync(session), CancellationToken.None)
            : Task.Run(() => RunDiscoveryAsync(session), CancellationToken.None);

        lock (session.SyncRoot)
        {
            var snapshot = ComputeProgressSnapshot(session, nowUtc);
            session.Status = snapshot.WorkflowStatus;
            session.UpdatedAtUtc = nowUtc;
            return Task.FromResult(Result.Success(ToSessionResponse(session, snapshot)));
        }
    }

    /// <summary>
    /// The background half of start + poll. Runs discovery in its OWN scope, then moves the session to
    /// exactly one terminal phase. Never throws: an unhandled exception on a detached task would take
    /// the process down and leave the session stuck in <c>in_progress</c> forever.
    /// </summary>
    private async Task RunDiscoveryAsync(WorkflowSessionState session)
    {
        // NOT the request's token. That token is precisely what the browser or proxy aborts, and an
        // aborted request never stopped the Apify run — it only stopped us from reading its answer.
        // Only the host shutting down may cancel this work.
        var ct = _applicationLifetime.ApplicationStopping;

        try
        {
            // Discovery is SCOPED and this service is a SINGLETON, so the background task must build
            // its own scope; capturing the caller's scoped services across the await would use a
            // provider that is disposed the moment the HTTP request ends.
            using var scope = _scopeFactory.CreateScope();
            var discovery = scope.ServiceProvider.GetRequiredService<IContentDiscoveryService>();

            // The session's normalized SearchCriterion drives which sources the chain consults
            // (CD-03/D4).
            var plan = await discovery.DiscoverAsync(
                new ContentDiscoveryRequest(
                    session.OrganizationId,
                    session.ClientId,
                    session.NetworkSources,
                    DefaultResultCount,
                    session.SearchCriterion,
                    // Carried verbatim: only an operator who explicitly asked for a refresh may cause a
                    // billed provider run.
                    session.ForceRefresh),
                ct);

            var results = ProjectCandidates(plan, session.SearchCriterion, session.StrategyMode);

            lock (session.SyncRoot)
            {
                session.Results = results;
                session.Phase = ResolveTerminalPhase(plan);
                session.ResultOrigin = plan.Origin == DiscoveryOrigin.Bank ? "bank" : "fresh";
                session.BankObservedAtUtc = plan.BankObservedAtUtc;
                session.UpdatedAtUtc = _clock.UtcNow;
            }

            _logger.LogInformation(
                "Content workflow discovery finished for {WorkflowId} with phase {Phase} and {CandidateCount} candidate(s).",
                session.WorkflowId,
                session.Phase,
                results.Count);
        }
        catch (Exception ex)
        {
            // Reported as failed, never as empty: "the provider is down" and "there was nothing to
            // find" are different answers and the operator acts differently on each.
            lock (session.SyncRoot)
            {
                session.Phase = DiscoveryPhase.Failed;
                session.UpdatedAtUtc = _clock.UtcNow;
            }

            _logger.LogError(
                ex,
                "Content workflow discovery failed for {WorkflowId} (client {ClientId}, criterion {SearchCriterion}).",
                session.WorkflowId,
                session.ClientId,
                session.SearchCriterion);
        }
    }

    /// <summary>
    /// The background half of start + poll for the <c>ai-ideas</c> mode: topics the model invents from
    /// the client's own context, with no provider call behind them.
    ///
    /// <para>
    /// Two things make this mode what the operator asked for. The chain is filtered to
    /// <see cref="IdeaOrigin.Invented"/>, so a source that scrapes is never even called — a batch that
    /// cannot be filled comes back short rather than quietly topped up from somebody's winning posts.
    /// And each run carries its OWN variation seed, so asking twice for the same client does not return
    /// the same list.
    /// </para>
    ///
    /// <para>
    /// The client's aesthetic is untouched here. Where a topic comes from and how the piece is designed
    /// are different layers: the aesthetic is applied downstream at generation, exactly as it is for a
    /// scraped run.
    /// </para>
    /// </summary>
    private async Task RunIdeaDrawAsync(WorkflowSessionState session)
    {
        // Same reasoning as RunDiscoveryAsync: never the request's token, which the browser aborts.
        var ct = _applicationLifetime.ApplicationStopping;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ideas = scope.ServiceProvider.GetRequiredService<ContentIdeaChain>();

            var plan = await ideas.DrawAsync(
                new ContentIdeaRequest(
                    session.OrganizationId,
                    session.ClientId,
                    DefaultResultCount,
                    OriginFilter: IdeaOrigin.Invented,
                    // Per SESSION, not per client: this is the whole "spontaneous, of that moment" part.
                    VariationSeed: session.WorkflowId.ToString("N")),
                ct);

            var results = ProjectIdeas(plan, session.SearchCriterion, session.StrategyMode);

            lock (session.SyncRoot)
            {
                session.Results = results;
                session.Phase = ResolveIdeaTerminalPhase(plan);
                // Neither "bank" nor "fresh": both describe scraped candidates, and nothing was scraped.
                session.ResultOrigin = "ai-ideas";
                session.BankObservedAtUtc = null;
                session.UpdatedAtUtc = _clock.UtcNow;
            }

            _logger.LogInformation(
                "Content workflow idea draw finished for {WorkflowId} with phase {Phase}, {IdeaCount} idea(s) and shortfall {Shortfall}.",
                session.WorkflowId,
                session.Phase,
                results.Count,
                plan.Shortfall);
        }
        catch (Exception ex)
        {
            lock (session.SyncRoot)
            {
                session.Phase = DiscoveryPhase.Failed;
                session.UpdatedAtUtc = _clock.UtcNow;
            }

            _logger.LogError(
                ex,
                "Content workflow idea draw failed for {WorkflowId} (client {ClientId}).",
                session.WorkflowId,
                session.ClientId);
        }
    }

    /// <summary>
    /// An idea draw has no "unconfigured" state — there is no provider to configure. It either produced
    /// topics, or every allowed source failed.
    /// </summary>
    private static DiscoveryPhase ResolveIdeaTerminalPhase(ContentIdeaPlan plan) =>
        plan.Ideas.Count > 0 ? DiscoveryPhase.Completed
        : plan.FailedSourceKeys.Count > 0 ? DiscoveryPhase.Failed
        : DiscoveryPhase.Completed;

    /// <summary>
    /// Projects invented topics onto the workflow's result shape, so selection, actions and package
    /// generation work on them unchanged.
    ///
    /// Every metric is zero and both URLs are empty ON PURPOSE. An invented topic has no audience and no
    /// post behind it; inventing a view count would let it outrank a genuinely winning scraped candidate
    /// in a screen that sorts on exactly that.
    /// </summary>
    private static IReadOnlyList<WorkflowResultState> ProjectIdeas(
        ContentIdeaPlan plan,
        string searchCriterion,
        string strategyMode) =>
        plan.Ideas
            .Select(idea => new WorkflowResultState(
                Guid.NewGuid(),
                idea.Title,
                // The idea's own source key, not a network: nothing here came from a network.
                idea.SourceKey,
                searchCriterion,
                strategyMode,
                idea.Reference ?? string.Empty,
                string.Empty,
                idea.Summary ?? string.Empty,
                0,
                0,
                0))
            .ToList();

    /// <summary>
    /// Collapses one plan onto the single terminal phase the operator sees. Candidates win over every
    /// partial failure — a run that produced something is not a failed run.
    /// </summary>
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

        // Ran, and genuinely found nothing. An empty proposal is reported as empty: falling back to
        // invented rows would hide a missing Apify route or a client with no references behind data
        // that looks real.
        return DiscoveryPhase.Completed;
    }

    /// <summary>Projects the chain's findings onto the workflow's result shape.</summary>
    private static IReadOnlyList<WorkflowResultState> ProjectCandidates(
        ContentDiscoveryPlan plan,
        string searchCriterion,
        string strategyMode) =>
        plan.Candidates
            .Select(item => new WorkflowResultState(
                Guid.NewGuid(),
                item.Title,
                item.NetworkSource,
                searchCriterion,
                strategyMode,
                item.ContentUrl,
                item.ThumbnailUrl ?? string.Empty,
                item.Summary,
                // The workflow contract predates these being real and types them as int; the source
                // values are long. Clamp rather than wrap, so an unusually viral post cannot come back
                // as a negative view count.
                ClampToInt(item.Views),
                ClampToInt(item.Likes),
                ClampToInt(item.Comments)))
            .ToList();

    // Metrics are nullable at the port now (D2): a network that does not report a metric renders as
    // 0 on this int-typed workflow contract, same as it always has for "not scraped yet".
    private static int ClampToInt(long? value) =>
        value is null or <= 0 ? 0 : value >= int.MaxValue ? int.MaxValue : (int)value;

    public Task<Result<ContentWorkflowStatusResponse>> GetStatusAsync(
        Guid organizationId,
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        var sessionResult = GetOwnedSession(organizationId, workflowId);
        if (sessionResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<ContentWorkflowStatusResponse>(sessionResult.Error));
        }

        var nowUtc = _clock.UtcNow;
        lock (sessionResult.Value.SyncRoot)
        {
            var snapshot = ComputeProgressSnapshot(sessionResult.Value, nowUtc);
            sessionResult.Value.Status = snapshot.WorkflowStatus;
            sessionResult.Value.UpdatedAtUtc = nowUtc;

            return Task.FromResult(Result.Success(new ContentWorkflowStatusResponse(
                sessionResult.Value.WorkflowId,
                snapshot.WorkflowStatus,
                snapshot.ProgressPercentage,
                snapshot.Message,
                snapshot.Steps,
                sessionResult.Value.UpdatedAtUtc,
                sessionResult.Value.Results.Count,
                sessionResult.Value.ResultOrigin,
                sessionResult.Value.BankObservedAtUtc)));
        }
    }

    public Task<Result<PageResult<ContentWorkflowResultResponse>>> GetResultsAsync(
        Guid organizationId,
        Guid workflowId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page <= 0 || pageSize <= 0)
        {
            return Task.FromResult(Result.Failure<PageResult<ContentWorkflowResultResponse>>(ContentWorkflowErrors.SelectionRequired));
        }

        var sessionResult = GetOwnedSession(organizationId, workflowId);
        if (sessionResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<PageResult<ContentWorkflowResultResponse>>(sessionResult.Error));
        }

        var nowUtc = _clock.UtcNow;
        lock (sessionResult.Value.SyncRoot)
        {
            var snapshot = ComputeProgressSnapshot(sessionResult.Value, nowUtc);
            sessionResult.Value.Status = snapshot.WorkflowStatus;
            sessionResult.Value.UpdatedAtUtc = nowUtc;

            var ordered = sessionResult.Value.Results
                .OrderByDescending(x => x.Views)
                .ThenBy(x => x.Title)
                .Select(result => ToResultResponse(result, sessionResult.Value.SelectedResultIds.Contains(result.Id)))
                .ToList();

            var pageItems = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var envelope = new PageResult<ContentWorkflowResultResponse>(
                pageItems,
                page,
                pageSize,
                ordered.Count);

            return Task.FromResult(Result.Success(envelope));
        }
    }

    public Task<Result<ContentWorkflowPreviewResponse>> GetPreviewAsync(
        Guid organizationId,
        Guid workflowId,
        Guid resultId,
        CancellationToken cancellationToken = default)
    {
        var sessionResult = GetOwnedSession(organizationId, workflowId);
        if (sessionResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<ContentWorkflowPreviewResponse>(sessionResult.Error));
        }

        lock (sessionResult.Value.SyncRoot)
        {
            var result = sessionResult.Value.Results.FirstOrDefault(x => x.Id == resultId);
            if (result is null)
            {
                return Task.FromResult(Result.Failure<ContentWorkflowPreviewResponse>(ContentWorkflowErrors.ResultNotFound));
            }

            var analysis = string.Join('\n',
            [
                $"Network: {result.NetworkSource}",
                $"Hook pattern: {result.SearchCriterion} opener focused on the first 3 seconds.",
                $"Content angle: {result.StrategyMode} with practical pacing and clear CTA.",
                $"Visual pattern: quick cuts + subtitle blocks + highlighted keyword moments.",
                $"Performance signals: {result.Views:N0} views, {result.Likes:N0} likes, {result.Comments:N0} comments."
            ]);

            var script = string.Join('\n',
            [
                "1. Hook (0s-3s): open with the strongest promise/result.",
                "2. Context (3s-8s): explain why this matters now.",
                "3. Core body (8s-30s): deliver 3 concise points with one visual cue each.",
                "4. CTA (final 5s): ask the user to save/comment/share with one explicit next action."
            ]);

            return Task.FromResult(Result.Success(new ContentWorkflowPreviewResponse(
                sessionResult.Value.WorkflowId,
                result.Id,
                result.Title,
                analysis,
                script)));
        }
    }

    public Task<Result<ContentWorkflowSelectionResponse>> SetSelectionAsync(
        Guid organizationId,
        Guid workflowId,
        IReadOnlyList<Guid> resultIds,
        CancellationToken cancellationToken = default)
    {
        if (resultIds.Count == 0)
        {
            return Task.FromResult(Result.Failure<ContentWorkflowSelectionResponse>(ContentWorkflowErrors.SelectionRequired));
        }

        var sessionResult = GetOwnedSession(organizationId, workflowId);
        if (sessionResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<ContentWorkflowSelectionResponse>(sessionResult.Error));
        }

        lock (sessionResult.Value.SyncRoot)
        {
            var knownIds = sessionResult.Value.Results.Select(x => x.Id).ToHashSet();
            var normalizedSelection = resultIds.Distinct().ToList();
            if (normalizedSelection.Any(id => !knownIds.Contains(id)))
            {
                return Task.FromResult(Result.Failure<ContentWorkflowSelectionResponse>(ContentWorkflowErrors.ResultNotFound));
            }

            sessionResult.Value.SelectedResultIds.Clear();
            foreach (var resultId in normalizedSelection)
            {
                sessionResult.Value.SelectedResultIds.Add(resultId);
            }

            sessionResult.Value.UpdatedAtUtc = _clock.UtcNow;

            return Task.FromResult(Result.Success(new ContentWorkflowSelectionResponse(
                sessionResult.Value.WorkflowId,
                sessionResult.Value.SelectedResultIds.Count,
                sessionResult.Value.SelectedResultIds.OrderBy(x => x).ToList(),
                sessionResult.Value.UpdatedAtUtc)));
        }
    }

    public Task<Result<ContentWorkflowActionResponse>> RunActionAsync(
        Guid organizationId,
        Guid workflowId,
        string action,
        IReadOnlyList<Guid> resultIds,
        CancellationToken cancellationToken = default)
    {
        var normalizedAction = ContentWorkflowCatalog.NormalizeActionType(action);
        if (normalizedAction.Length == 0)
        {
            return Task.FromResult(Result.Failure<ContentWorkflowActionResponse>(ContentWorkflowErrors.ActionInvalid));
        }

        if (resultIds.Count == 0)
        {
            return Task.FromResult(Result.Failure<ContentWorkflowActionResponse>(ContentWorkflowErrors.SelectionRequired));
        }

        var sessionResult = GetOwnedSession(organizationId, workflowId);
        if (sessionResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<ContentWorkflowActionResponse>(sessionResult.Error));
        }

        lock (sessionResult.Value.SyncRoot)
        {
            var knownIds = sessionResult.Value.Results.Select(x => x.Id).ToHashSet();
            var normalizedIds = resultIds.Distinct().ToList();
            if (normalizedIds.Any(id => !knownIds.Contains(id)))
            {
                return Task.FromResult(Result.Failure<ContentWorkflowActionResponse>(ContentWorkflowErrors.ResultNotFound));
            }

            var nowUtc = _clock.UtcNow;
            var response = new ContentWorkflowActionResponse(
                Guid.NewGuid(),
                sessionResult.Value.WorkflowId,
                normalizedAction,
                "completed",
                normalizedIds,
                BuildActionMessage(normalizedAction, normalizedIds.Count),
                nowUtc);

            sessionResult.Value.Actions.Add(response);
            sessionResult.Value.UpdatedAtUtc = nowUtc;

            return Task.FromResult(Result.Success(response));
        }
    }

    public async Task<Result<ContentWorkflowPackageResponse>> GeneratePackageAsync(
        Guid organizationId,
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        var sessionResult = GetOwnedSession(organizationId, workflowId);
        if (sessionResult.IsFailure)
        {
            return Result.Failure<ContentWorkflowPackageResponse>(sessionResult.Error);
        }

        // Snapshot what was selected under the lock, then start generation OUTSIDE it: queuing touches
        // the database and the job queue, and holding a session lock across that would block every
        // other call on this workflow for the duration.
        List<SelectedProposal> proposals;
        Guid clientId;

        lock (sessionResult.Value.SyncRoot)
        {
            if (sessionResult.Value.SelectedResultIds.Count == 0)
            {
                return Result.Failure<ContentWorkflowPackageResponse>(ContentWorkflowErrors.PackageSelectionRequired);
            }

            clientId = sessionResult.Value.ClientId;
            proposals = sessionResult.Value.Results
                .Where(result => sessionResult.Value.SelectedResultIds.Contains(result.Id))
                .Select(result => new SelectedProposal(result.Title, result.Summary, result.VideoUrl))
                .ToList();
        }

        ProposalGenerationStarted started;

        using (var scope = _scopeFactory.CreateScope())
        {
            var starter = scope.ServiceProvider.GetRequiredService<IProposalGenerationStarter>();

            started = await starter.StartAsync(
                new ProposalGenerationRequest(
                    organizationId,
                    clientId,
                    proposals,
                    // Default composition until the screen exposes the cart. Script plus feed carousel
                    // is the pairing the operator picks by hand today.
                    DefaultProposalFormats),
                cancellationToken);
        }

        lock (sessionResult.Value.SyncRoot)
        {

            var nowUtc = _clock.UtcNow;
            var fileName = $"content-plan-{sessionResult.Value.WorkflowId:N}.zip";

            // The pieces are QUEUED, not ready: they drain through the content generation worker and
            // land in the content bank. Saying "ready" here would send the operator looking for a
            // download that does not exist yet.
            var package = new ContentWorkflowPackageResponse(
                sessionResult.Value.WorkflowId,
                fileName,
                $"/api/social/content-generation-batches/{started.BatchId}/progress",
                "generation_queued",
                started.TotalPieces,
                nowUtc);

            sessionResult.Value.Actions.Add(new ContentWorkflowActionResponse(
                Guid.NewGuid(),
                sessionResult.Value.WorkflowId,
                "generate_package",
                "completed",
                sessionResult.Value.SelectedResultIds.OrderBy(x => x).ToList(),
                $"Queued {started.TotalPieces} piece(s) from {proposals.Count} selected proposal(s).",
                nowUtc));

            sessionResult.Value.UpdatedAtUtc = nowUtc;
            return Result.Success(package);
        }
    }

    public Task<Result<ContentWorkflowHistoryResponse>> GetHistoryAsync(
        Guid organizationId,
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        var sessionResult = GetOwnedSession(organizationId, workflowId);
        if (sessionResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<ContentWorkflowHistoryResponse>(sessionResult.Error));
        }

        lock (sessionResult.Value.SyncRoot)
        {
            var summary = ToHistoryItem(sessionResult.Value);
            var actions = sessionResult.Value.Actions
                .OrderByDescending(x => x.ExecutedAtUtc)
                .ToList();

            return Task.FromResult(Result.Success(new ContentWorkflowHistoryResponse(summary, actions)));
        }
    }

    public Task<Result<IReadOnlyList<ContentWorkflowHistoryItemResponse>>> ListHistoryAsync(
        Guid organizationId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<ContentWorkflowHistoryItemResponse>>(ContentWorkflowErrors.OrganizationRequired));
        }

        var normalizedLimit = limit <= 0 ? HistoryDefaultLimit : Math.Min(limit, 50);

        var items = _sessions.Values
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(normalizedLimit)
            .Select(ToHistoryItem)
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<ContentWorkflowHistoryItemResponse>>(items));
    }

    private Result<WorkflowSessionState> GetOwnedSession(Guid organizationId, Guid workflowId)
    {
        if (organizationId == Guid.Empty)
        {
            return Result.Failure<WorkflowSessionState>(ContentWorkflowErrors.OrganizationRequired);
        }

        if (!_sessions.TryGetValue(workflowId, out var session) || session.OrganizationId != organizationId)
        {
            return Result.Failure<WorkflowSessionState>(ContentWorkflowErrors.NotFound);
        }

        return Result.Success(session);
    }


    private static ContentWorkflowResultResponse ToResultResponse(WorkflowResultState result, bool isSelected) =>
        new(
            result.Id,
            result.Title,
            result.NetworkSource,
            result.SearchCriterion,
            result.StrategyMode,
            result.VideoUrl,
            result.ThumbnailUrl,
            result.Summary,
            result.Views,
            result.Likes,
            result.Comments,
            isSelected);

    private static ContentWorkflowHistoryItemResponse ToHistoryItem(WorkflowSessionState session) =>
        new(
            session.WorkflowId,
            session.ClientId,
            session.ClientName,
            session.SearchCriterion,
            session.StrategyMode,
            session.NetworkSources,
            session.Status,
            session.Results.Count,
            session.SelectedResultIds.Count,
            session.CreatedAtUtc,
            session.UpdatedAtUtc);

    private static string BuildActionMessage(string action, int itemCount) => action switch
    {
        "download_audio" => $"Audio download completed for {itemCount} content item(s).",
        "transcribe_and_build_script" => $"Transcription and script draft completed for {itemCount} content item(s).",
        "build_carousel" => $"Carousel draft generated for {itemCount} content item(s).",
        _ => $"Action '{action}' completed for {itemCount} content item(s).",
    };

    private static ContentWorkflowSessionResponse ToSessionResponse(
        WorkflowSessionState session,
        ProgressSnapshot snapshot) =>
        new(
            session.WorkflowId,
            session.ClientId,
            session.ClientName,
            session.SearchCriterion,
            session.StrategyMode,
            session.NetworkSources,
            snapshot.WorkflowStatus,
            snapshot.ProgressPercentage,
            snapshot.Message,
            session.Results.Count,
            session.SelectedResultIds.Count,
            session.CreatedAtUtc,
            session.UpdatedAtUtc);

    /// <summary>
    /// Progress is now driven by the session's discovery phase, not by the clock alone. While the
    /// scrape runs, elapsed time only animates the step list and is capped below 100 so the bar can
    /// never announce a completion that has not happened; the phase alone decides the terminal state.
    /// </summary>
    private static ProgressSnapshot ComputeProgressSnapshot(WorkflowSessionState session, DateTime nowUtc)
    {
        var phases = BuildProgressPhases(session.NetworkSources);
        var totalDuration = Math.Max(ProgressPhaseDurationSeconds, phases.Count * ProgressPhaseDurationSeconds);
        var elapsedSeconds = Math.Max(0, (nowUtc - session.CreatedAtUtc).TotalSeconds);
        var normalizedRatio = Math.Min(1d, elapsedSeconds / totalDuration);
        var rampPercentage = (int)Math.Round(normalizedRatio * 100, MidpointRounding.AwayFromZero);

        var isRunning = session.Phase == DiscoveryPhase.InProgress;
        var progress = isRunning ? Math.Min(MaxInProgressPercentage, rampPercentage) : 100;

        var activeIndex = isRunning
            ? Math.Clamp((int)Math.Floor(elapsedSeconds / ProgressPhaseDurationSeconds), 0, phases.Count - 1)
            : phases.Count - 1;

        var terminalStepStatus = session.Phase switch
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

            steps.Add(new ContentWorkflowStepResponse(
                phases[i].Key,
                phases[i].Label,
                stepStatus,
                phases[i].Message,
                stepProgress));
        }

        var message = session.Phase switch
        {
            DiscoveryPhase.InProgress => phases[activeIndex].Message,
            DiscoveryPhase.Failed =>
                "Content discovery failed. The provider could not be reached or rejected the run.",
            DiscoveryPhase.Unconfigured =>
                "Content discovery is not configured for the selected network. Map or route a search actor under Settings / Integrations.",
            _ when session.Results.Count == 0 =>
                "Content discovery finished and found no candidates.",
            _ => "Content retrieval and analysis completed.",
        };

        return new ProgressSnapshot(progress, StatusFor(session.Phase), message, steps);
    }

    /// <summary>
    /// The four states the operator can be in. "Completed with zero results" stays <c>completed</c>:
    /// the run succeeded and found nothing, which the caller reads off the result count.
    /// </summary>
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
            phases.Add(new ProgressPhase(
                $"retrieve-{network}",
                $"Retrieve from {network}",
                ProviderProgressMessage(network)));
        }

        phases.Add(new ProgressPhase(
            "analyze",
            "AI analysis and script drafting",
            "Generating video analysis and script drafts for preview tabs..."));

        phases.Add(new ProgressPhase(
            "finalize",
            "Finalize cards and pagination",
            "Preparing paginated result cards and selection state..."));

        return phases;
    }

    private static string ProviderProgressMessage(string networkSource) => networkSource switch
    {
        "tiktok" => "Retrieving videos from TikTok API...",
        "instagram" => "Retrieving videos from Instagram via Apify adapter...",
        "youtube" => "Retrieving trend candidates through Data365 adapter...",
        _ => $"Retrieving content from {networkSource} provider adapter...",
    };

    /// <summary>
    /// The terminal outcomes are kept apart on purpose: "the provider errored", "no actor is routed
    /// for this network" and "it ran and found nothing" are three different problems with three
    /// different fixes, and collapsing them is what made a provider failure read as an empty result.
    /// </summary>
    private enum DiscoveryPhase
    {
        InProgress,
        Completed,
        Failed,
        Unconfigured,
    }

    private sealed class WorkflowSessionState
    {
        public WorkflowSessionState(
            Guid workflowId,
            Guid organizationId,
            Guid clientId,
            string clientName,
            string searchCriterion,
            string strategyMode,
            IReadOnlyList<string> networkSources,
            bool forceRefresh,
            DateTime createdAtUtc,
            DateTime updatedAtUtc,
            string status,
            IReadOnlyList<WorkflowResultState> results,
            HashSet<Guid> selectedResultIds,
            List<ContentWorkflowActionResponse> actions,
            string originMode)
        {
            OriginMode = originMode;
            WorkflowId = workflowId;
            OrganizationId = organizationId;
            ClientId = clientId;
            ClientName = clientName;
            SearchCriterion = searchCriterion;
            StrategyMode = strategyMode;
            NetworkSources = networkSources;
            ForceRefresh = forceRefresh;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            Status = status;
            Results = results;
            SelectedResultIds = selectedResultIds;
            Actions = actions;
        }

        public object SyncRoot { get; } = new();

        public Guid WorkflowId { get; }

        public Guid OrganizationId { get; }

        public Guid ClientId { get; }

        public string ClientName { get; }

        public string SearchCriterion { get; }

        public string StrategyMode { get; }

        public IReadOnlyList<string> NetworkSources { get; }

        /// <summary>
        /// <c>discovery</c> or <c>ai-ideas</c>, captured at start. Decides which background routine the
        /// session ran and therefore whether any provider could have been touched at all.
        /// </summary>
        public string OriginMode { get; }

        /// <summary>
        /// The operator's explicit opt-in to a billed provider run, captured at start and never
        /// re-derived: a session must not turn into a paid scrape after the fact.
        /// </summary>
        public bool ForceRefresh { get; }

        /// <summary>
        /// <c>bank</c>, <c>fresh</c>, or empty while discovery is still running. Written once by the
        /// background task under <see cref="SyncRoot"/>, like <see cref="Phase"/>.
        /// </summary>
        public string ResultOrigin { get; set; } = string.Empty;

        /// <summary>When the OLDEST banked candidate was observed; <c>null</c> for a fresh run.</summary>
        public DateTimeOffset? BankObservedAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; }

        public DateTime UpdatedAtUtc { get; set; }

        public string Status { get; set; }

        /// <summary>
        /// Where the background discovery got to. Written once by the background task under
        /// <see cref="SyncRoot"/>; every read path takes the same lock.
        /// </summary>
        public DiscoveryPhase Phase { get; set; } = DiscoveryPhase.InProgress;

        public IReadOnlyList<WorkflowResultState> Results { get; set; }

        public HashSet<Guid> SelectedResultIds { get; }

        public List<ContentWorkflowActionResponse> Actions { get; }
    }

    private sealed record WorkflowResultState(
        Guid Id,
        string Title,
        string NetworkSource,
        string SearchCriterion,
        string StrategyMode,
        string VideoUrl,
        string ThumbnailUrl,
        string Summary,
        int Views,
        int Likes,
        int Comments);

    private sealed record ProgressPhase(string Key, string Label, string Message);

    private sealed record ProgressSnapshot(
        int ProgressPercentage,
        string WorkflowStatus,
        string Message,
        IReadOnlyList<ContentWorkflowStepResponse> Steps);
}
