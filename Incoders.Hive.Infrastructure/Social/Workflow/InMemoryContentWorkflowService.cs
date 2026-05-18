using System.Collections.Concurrent;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Application.Features.Social.Workflow;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Workflow;

namespace Incoders.Hive.Infrastructure.Social.Workflow;

public sealed class InMemoryContentWorkflowService : IContentWorkflowService
{
    private const int DefaultResultCount = 72;
    private const int HistoryDefaultLimit = 12;
    private const double ProgressPhaseDurationSeconds = 2.2;

    private readonly IClock _clock;
    private readonly ConcurrentDictionary<Guid, WorkflowSessionState> _sessions = new();

    public InMemoryContentWorkflowService(IClock clock)
    {
        _clock = clock;
    }

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

        var normalizedNetworks = ContentWorkflowCatalog.NormalizeNetworkSources(input.NetworkSources);
        if (normalizedNetworks.Count == 0)
        {
            return Task.FromResult(Result.Failure<ContentWorkflowSessionResponse>(ContentWorkflowErrors.NetworkSourcesRequired));
        }

        var nowUtc = _clock.UtcNow;
        var workflowId = Guid.NewGuid();
        var results = GenerateResults(
            workflowId,
            searchCriterion,
            strategyMode,
            normalizedNetworks,
            DefaultResultCount);

        var session = new WorkflowSessionState(
            workflowId,
            organizationId,
            input.ClientId,
            input.ClientName.Trim(),
            searchCriterion,
            strategyMode,
            normalizedNetworks,
            nowUtc,
            nowUtc,
            "in_progress",
            results,
            new HashSet<Guid>(),
            new List<ContentWorkflowActionResponse>());

        _sessions[workflowId] = session;

        lock (session.SyncRoot)
        {
            var snapshot = ComputeProgressSnapshot(session, nowUtc);
            session.Status = snapshot.WorkflowStatus;
            session.UpdatedAtUtc = nowUtc;
            return Task.FromResult(Result.Success(ToSessionResponse(session, snapshot)));
        }
    }

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
                sessionResult.Value.UpdatedAtUtc)));
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

    public Task<Result<ContentWorkflowPackageResponse>> GeneratePackageAsync(
        Guid organizationId,
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        var sessionResult = GetOwnedSession(organizationId, workflowId);
        if (sessionResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<ContentWorkflowPackageResponse>(sessionResult.Error));
        }

        lock (sessionResult.Value.SyncRoot)
        {
            if (sessionResult.Value.SelectedResultIds.Count == 0)
            {
                return Task.FromResult(Result.Failure<ContentWorkflowPackageResponse>(ContentWorkflowErrors.PackageSelectionRequired));
            }

            var nowUtc = _clock.UtcNow;
            var fileName = $"content-plan-{sessionResult.Value.WorkflowId:N}.zip";
            var package = new ContentWorkflowPackageResponse(
                sessionResult.Value.WorkflowId,
                fileName,
                $"/downloads/simulated/{fileName}",
                "simulated_ready",
                sessionResult.Value.SelectedResultIds.Count,
                nowUtc);

            sessionResult.Value.Actions.Add(new ContentWorkflowActionResponse(
                Guid.NewGuid(),
                sessionResult.Value.WorkflowId,
                "generate_package",
                "completed",
                sessionResult.Value.SelectedResultIds.OrderBy(x => x).ToList(),
                $"Generated simulated package with {sessionResult.Value.SelectedResultIds.Count} selected contents.",
                nowUtc));

            sessionResult.Value.UpdatedAtUtc = nowUtc;
            return Task.FromResult(Result.Success(package));
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

    private static IReadOnlyList<WorkflowResultState> GenerateResults(
        Guid workflowId,
        string searchCriterion,
        string strategyMode,
        IReadOnlyList<string> networkSources,
        int count)
    {
        var results = new List<WorkflowResultState>(count);
        var seed = workflowId.GetHashCode();
        var random = new Random(seed);
        var networks = networkSources.Count == 0 ? new[] { "tiktok" } : networkSources.ToArray();

        for (var index = 0; index < count; index++)
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            var id = new Guid(bytes);
            var network = networks[index % networks.Length];
            var views = random.Next(7_500, 250_000);
            var likes = Math.Max(120, views / random.Next(8, 17));
            var comments = Math.Max(12, likes / random.Next(4, 9));

            results.Add(new WorkflowResultState(
                id,
                $"{searchCriterion} {strategyMode} concept #{index + 1}",
                network,
                searchCriterion,
                strategyMode,
                $"https://example.com/{network}/video/{id:N}",
                $"https://picsum.photos/seed/{id:N}/640/360",
                $"Top-performing {strategyMode} candidate for {searchCriterion} exploration.",
                views,
                likes,
                comments));
        }

        return results;
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

    private static ProgressSnapshot ComputeProgressSnapshot(WorkflowSessionState session, DateTime nowUtc)
    {
        var phases = BuildProgressPhases(session.NetworkSources);
        var totalDuration = Math.Max(ProgressPhaseDurationSeconds, phases.Count * ProgressPhaseDurationSeconds);
        var elapsedSeconds = Math.Max(0, (nowUtc - session.CreatedAtUtc).TotalSeconds);
        var normalizedRatio = Math.Min(1d, elapsedSeconds / totalDuration);
        var progress = (int)Math.Round(normalizedRatio * 100, MidpointRounding.AwayFromZero);
        var isCompleted = progress >= 100;

        var activeIndex = isCompleted
            ? phases.Count - 1
            : Math.Clamp((int)Math.Floor(elapsedSeconds / ProgressPhaseDurationSeconds), 0, phases.Count - 1);

        var steps = new List<ContentWorkflowStepResponse>(phases.Count);
        for (var i = 0; i < phases.Count; i++)
        {
            var stepProgress = 0;
            var stepStatus = "pending";

            if (i < activeIndex || isCompleted)
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

        var message = isCompleted
            ? "Content retrieval and analysis completed."
            : phases[activeIndex].Message;

        return new ProgressSnapshot(
            progress,
            isCompleted ? "completed" : "in_progress",
            message,
            steps);
    }

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
            DateTime createdAtUtc,
            DateTime updatedAtUtc,
            string status,
            IReadOnlyList<WorkflowResultState> results,
            HashSet<Guid> selectedResultIds,
            List<ContentWorkflowActionResponse> actions)
        {
            WorkflowId = workflowId;
            OrganizationId = organizationId;
            ClientId = clientId;
            ClientName = clientName;
            SearchCriterion = searchCriterion;
            StrategyMode = strategyMode;
            NetworkSources = networkSources;
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

        public DateTime CreatedAtUtc { get; }

        public DateTime UpdatedAtUtc { get; set; }

        public string Status { get; set; }

        public IReadOnlyList<WorkflowResultState> Results { get; }

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
