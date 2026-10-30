using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.DiscoveryBankIntelligence;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace SocialStudio.Application.Features.Social.DiscoveryBankIntelligence;

/// <summary>One operator question about the discovery candidate bank, with the chat so far.</summary>
public sealed record AskDiscoveryBankQuestionQuery(
    Guid OrganizationId,
    string Question,
    IReadOnlyList<DiscoveryBankChatTurn> History) : IRequest<Result<DiscoveryBankAnswerResponse>>;

/// <summary><c>Role</c> is <c>user</c> or <c>assistant</c>; anything else is treated as <c>user</c>.</summary>
public sealed record DiscoveryBankChatTurn(string Role, string Text);

public enum DiscoveryBankAnswerStatus
{
    /// <summary>A catalog query ran and the answer is backed by the cited candidates.</summary>
    Answered = 0,

    /// <summary>The question maps to no supported query. Nothing ran; nothing was guessed.</summary>
    Unsupported = 1,

    /// <summary>The organization has never banked a candidate. Nothing to interrogate yet.</summary>
    BankEmpty = 2,
}

/// <summary>Exactly which catalog query answered the question, so the operator can check the reading.</summary>
public sealed record DiscoveryBankExecutedQueryResponse(
    string QueryKey,
    string Label,
    string Metric,
    string? Aggregate,
    int Limit,
    string? NetworkKey,
    string? Hashtag,
    string? AuthorHandle,
    DateTimeOffset? PublishedFromUtc,
    DateTimeOffset? PublishedToUtc,
    string? Interpretation);

/// <summary>
/// One candidate the answer rests on. Carries the video URL and the handle without exception: a ranking
/// the operator cannot open and verify is not worth spending generation budget on.
/// </summary>
public sealed record DiscoveryBankCitationResponse(
    string CandidateKey,
    string NetworkKey,
    string ContentUrl,
    string AuthorHandle,
    string Title,
    IReadOnlyList<string> Hashtags,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset ObservedAtUtc,
    long? Views,
    long? Likes,
    long? Comments,
    long? Shares,
    long? Saves,
    double? OpportunityScore,
    double? MetricValue);

public sealed record DiscoveryBankGroupResponse(string Key, int CandidateCount, double MetricTotal);

public sealed record DiscoveryBankSupportedQueryResponse(
    string Key,
    string Label,
    string Description,
    string ExampleQuestion);

/// <summary>
/// The answer as the operator sees it. <c>MatchedCount</c> is reported next to the citations on purpose:
/// it is what turns "here are 10 candidates" into "here are the top 10 of 412", which is the difference
/// between a verifiable claim and an arbitrary sample.
/// </summary>
public sealed record DiscoveryBankAnswerResponse(
    string Question,
    DiscoveryBankAnswerStatus Status,
    string Answer,
    DiscoveryBankExecutedQueryResponse? ExecutedQuery,
    IReadOnlyList<DiscoveryBankCitationResponse> Citations,
    IReadOnlyList<DiscoveryBankGroupResponse> Groups,
    int MatchedCount,
    double? AggregateValue,
    IReadOnlyList<DiscoveryBankSupportedQueryResponse> SupportedQueries);

/// <summary>
/// Orchestrates one question: the MODEL plans and narrates, the SYSTEM computes.
///
/// Call 1 maps the question onto the enumerated catalog and gets NO candidate data — it only needs the
/// question, the chat so far and the list of supported queries. The plan is then validated against the
/// allowed domain; a plan that fails there ends the turn with an honest refusal and no second call.
///
/// The query is executed deterministically over the FULL bank. Call 2 narrates over ONLY the computed
/// rows, capped by <see cref="DiscoveryBankIntelligenceOptions.MaxRowsToModel"/>. The model therefore
/// never sees enough of the bank to rank it, which is the point: it cannot silently substitute its own
/// arithmetic for the query's.
/// </summary>
public sealed class AskDiscoveryBankQuestionHandler
    : IRequestHandler<AskDiscoveryBankQuestionQuery, Result<DiscoveryBankAnswerResponse>>
{
    private const string FlowKey = "single-analyzer";

    /// <summary>
    /// The AI route is REUSED from the Single Analyzer's content-analysis step rather than introducing a
    /// new workflow key. A new key would need its own integrations checklist group before it could be
    /// configured at all, so a fresh organization would meet "not configurable yet" on a tool that needs
    /// no new provider. Interrogating the bank is the same class of work on the same data the Single
    /// Analyzer already routes.
    /// </summary>
    private const string AnalysisTypeKey = "content-analysis";
    private const string StepKey = "analyze-content";
    private const string OperationKey = "ai-analysis";
    private const string CapabilityKey = "ai-analysis";
    private const string PlanPromptVersion = "discovery-bank.plan.v1";
    private const string NarrationPromptVersion = "discovery-bank.narration.v1";

    /// <summary>
    /// Shown verbatim when the bank is empty. It names the exact route that fills it, because "no data"
    /// with no next step is indistinguishable from a broken tool.
    /// </summary>
    internal const string EmptyBankAnswer =
        "There is no discovery data to analyse yet. This tool only reads the discovery candidate bank, " +
        "and nothing has been banked for this organization. Run a niche search first — Single Analyzer → " +
        "By Search — and the candidates it finds will appear here.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDiscoveryBankReader _bank;
    private readonly IAiCompletionGateway _aiGateway;
    private readonly IWorkflowConfigurationResolver _workflowResolver;
    private readonly DiscoveryBankIntelligenceOptions _options;
    private readonly ILogger<AskDiscoveryBankQuestionHandler> _logger;

    public AskDiscoveryBankQuestionHandler(
        IDiscoveryBankReader bank,
        IAiCompletionGateway aiGateway,
        IWorkflowConfigurationResolver workflowResolver,
        IOptions<DiscoveryBankIntelligenceOptions> options,
        ILogger<AskDiscoveryBankQuestionHandler>? logger = null)
    {
        _bank = bank;
        _aiGateway = aiGateway;
        _workflowResolver = workflowResolver;
        _options = options.Value;
        _logger = logger ?? NullLogger<AskDiscoveryBankQuestionHandler>.Instance;
    }

    public async Task<Result<DiscoveryBankAnswerResponse>> Handle(
        AskDiscoveryBankQuestionQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<DiscoveryBankAnswerResponse>(DiscoveryBankErrors.OrganizationRequired);
        }

        var question = request.Question?.Trim() ?? string.Empty;
        if (question.Length == 0)
        {
            return Result.Failure<DiscoveryBankAnswerResponse>(DiscoveryBankErrors.QuestionRequired);
        }

        if (question.Length > _options.MaxQuestionLength)
        {
            return Result.Failure<DiscoveryBankAnswerResponse>(DiscoveryBankErrors.QuestionTooLong);
        }

        var bank = await _bank.ListAsync(request.OrganizationId, cancellationToken);

        // An empty bank is a STATE, not a failure — and it is answered without spending a single token.
        if (bank.Count == 0)
        {
            return Result.Success(EmptyBank(question));
        }

        var routes = await ResolveAiRoutesAsync(request.OrganizationId, cancellationToken);
        if (routes.IsFailure)
        {
            return Result.Failure<DiscoveryBankAnswerResponse>(routes.Error);
        }

        var planInput = JsonSerializer.Serialize(
            BuildPlanInput(question, request.History, bank),
            JsonOptions);

        var plan = await CompleteAsync<DiscoveryBankQueryPlan>(
            request.OrganizationId,
            CapabilityKey,
            PlanPromptVersion,
            planInput,
            routes.Value,
            cancellationToken);

        if (plan.IsFailure)
        {
            _logger.LogWarning(
                "AI Analysis: planning call failed for org {OrganizationId}: {ErrorCode}.",
                request.OrganizationId,
                plan.Error.Code);
            return Result.Failure<DiscoveryBankAnswerResponse>(plan.Error);
        }

        var spec = DiscoveryBankQueryPlanValidator.Validate(plan.Value, _options);
        if (spec.IsFailure)
        {
            _logger.LogInformation(
                "AI Analysis: question refused for org {OrganizationId} ({ErrorCode}) — nothing was executed.",
                request.OrganizationId,
                spec.Error.Code);
            return Result.Success(Unsupported(question, plan.Value, spec.Error));
        }

        var computed = DiscoveryBankQueryExecutor.Execute(spec.Value, bank);

        var narrationInput = JsonSerializer.Serialize(
            BuildNarrationInput(question, computed),
            JsonOptions);

        var narration = await CompleteAsync<DiscoveryBankNarration>(
            request.OrganizationId,
            CapabilityKey,
            NarrationPromptVersion,
            narrationInput,
            routes.Value,
            cancellationToken);

        if (narration.IsFailure)
        {
            _logger.LogWarning(
                "AI Analysis: narration call failed for org {OrganizationId}: {ErrorCode}.",
                request.OrganizationId,
                narration.Error.Code);
            return Result.Failure<DiscoveryBankAnswerResponse>(DiscoveryBankErrors.NarrationFailed);
        }

        return Result.Success(Answered(question, plan.Value, computed, narration.Value.Answer));
    }

    // ── responses ────────────────────────────────────────────────────────────

    private static DiscoveryBankAnswerResponse EmptyBank(string question) => new(
        question,
        DiscoveryBankAnswerStatus.BankEmpty,
        EmptyBankAnswer,
        ExecutedQuery: null,
        Citations: [],
        Groups: [],
        MatchedCount: 0,
        AggregateValue: null,
        SupportedQueries: SupportedQueries());

    private static DiscoveryBankAnswerResponse Unsupported(
        string question,
        DiscoveryBankQueryPlan plan,
        Error error)
    {
        var reason = error.Code == DiscoveryBankErrors.InvalidQueryParameter.Code
            ? "That question uses a filter this tool cannot apply — the network, date or handle it named is not one the bank can be narrowed by."
            : "That question does not map to any analysis this tool can run over the discovery candidate bank.";

        var modelReason = string.IsNullOrWhiteSpace(plan.Unsupported) ? null : plan.Unsupported.Trim();

        var answer = string.Join(
            " ",
            new[]
            {
                reason,
                modelReason,
                "Here is what it can answer: " + string.Join(
                    "; ",
                    DiscoveryBankQueryCatalog.Entries.Select(entry => $"{entry.Label} (e.g. \"{entry.ExampleQuestion}\")")) + ".",
            }.Where(part => !string.IsNullOrWhiteSpace(part)));

        return new DiscoveryBankAnswerResponse(
            question,
            DiscoveryBankAnswerStatus.Unsupported,
            answer,
            ExecutedQuery: null,
            Citations: [],
            Groups: [],
            MatchedCount: 0,
            AggregateValue: null,
            SupportedQueries: SupportedQueries());
    }

    private static DiscoveryBankAnswerResponse Answered(
        string question,
        DiscoveryBankQueryPlan plan,
        DiscoveryBankQueryResult computed,
        string narration)
    {
        var entry = DiscoveryBankQueryCatalog.For(computed.Spec.Kind);
        var spec = computed.Spec;

        return new DiscoveryBankAnswerResponse(
            question,
            DiscoveryBankAnswerStatus.Answered,
            string.IsNullOrWhiteSpace(narration) ? entry.Description : narration.Trim(),
            new DiscoveryBankExecutedQueryResponse(
                entry.Key,
                entry.Label,
                MetricKey(spec.Metric),
                spec.Kind == DiscoveryBankQueryKind.AggregateMetric ? spec.Aggregate.ToString().ToLowerInvariant() : null,
                spec.Limit,
                spec.Filter.NetworkKey,
                spec.Filter.Hashtag,
                spec.Filter.AuthorHandle,
                spec.Filter.PublishedFromUtc,
                spec.Filter.PublishedToUtc,
                string.IsNullOrWhiteSpace(plan.Interpretation) ? null : plan.Interpretation.Trim()),
            computed.Rows.Select(ToCitation).ToList(),
            computed.Groups.Select(group => new DiscoveryBankGroupResponse(group.Key, group.CandidateCount, group.MetricTotal)).ToList(),
            computed.MatchedCount,
            computed.AggregateValue,
            SupportedQueries());
    }

    private static DiscoveryBankCitationResponse ToCitation(DiscoveryBankResultRow row) => new(
        row.CandidateKey,
        row.NetworkKey,
        row.ContentUrl,
        row.AuthorHandle,
        row.Title,
        row.Hashtags,
        row.PublishedAtUtc,
        row.ObservedAtUtc,
        row.Views,
        row.Likes,
        row.Comments,
        row.Shares,
        row.Saves,
        row.OpportunityScore,
        row.MetricValue);

    internal static IReadOnlyList<DiscoveryBankSupportedQueryResponse> SupportedQueries() =>
        DiscoveryBankQueryCatalog.Entries
            .Select(entry => new DiscoveryBankSupportedQueryResponse(
                entry.Key,
                entry.Label,
                entry.Description,
                entry.ExampleQuestion))
            .ToList();

    private static string MetricKey(DiscoveryBankMetric metric) => metric switch
    {
        DiscoveryBankMetric.OpportunityScore => "opportunity_score",
        _ => metric.ToString().ToLowerInvariant(),
    };

    // ── prompt inputs ────────────────────────────────────────────────────────

    /// <summary>
    /// The planner's whole world: the question, a trimmed history, the catalog, and a few facts about the
    /// SHAPE of the bank (size, networks, date span). No candidate rows — the planner has no business
    /// reading data it is not allowed to reason over.
    /// </summary>
    private object BuildPlanInput(
        string question,
        IReadOnlyList<DiscoveryBankChatTurn>? history,
        IReadOnlyList<DiscoveryBankRow> bank) => new
        {
            instruction =
                "Map the operator's question onto EXACTLY ONE supported query and its parameters. " +
                "You must NOT answer the question, rank anything, or invent data — the system computes " +
                "every number. If the question cannot be served by one of the supported queries, leave " +
                "queryKind empty and put a one-sentence explanation in 'unsupported'. Respond with ONLY " +
                "the raw JSON object.",
            question,
            history = TrimHistory(history),
            supportedQueries = DiscoveryBankQueryCatalog.Entries.Select(entry => new
            {
                queryKind = entry.Key,
                entry.Label,
                entry.Description,
                entry.ExampleQuestion,
            }),
            allowedMetrics = new[] { "views", "likes", "comments", "shares", "saves", "engagement", "opportunity_score" },
            allowedAggregates = new[] { "sum", "average", "maximum", "minimum" },
            allowedNetworks = new[] { "instagram", "tiktok", "youtube" },
            bankShape = new
            {
                candidateCount = bank.Count,
                networks = bank.Select(row => row.NetworkKey).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal),
                earliestObservedAtUtc = bank.Min(row => row.ObservedAtUtc),
                latestObservedAtUtc = bank.Max(row => row.ObservedAtUtc),
            },
            expectedOutput =
                "queryKind, metric, aggregate, limit, networkKey, hashtag, authorHandle, publishedFromUtc, " +
                "publishedToUtc, unsupported, interpretation. Dates must be ISO-8601 UTC.",
        };

    /// <summary>
    /// The narrator's whole world: the query that ran and the rows it returned. Nothing else — so the
    /// prose can only describe what was computed.
    /// </summary>
    private static object BuildNarrationInput(string question, DiscoveryBankQueryResult computed) => new
    {
        instruction =
            "Write two or three plain sentences answering the operator's question using ONLY the computed " +
            "results below. The system already did the arithmetic: never re-rank, re-count or estimate, and " +
            "never mention data that is not present here. Refer to candidates by their author handle. " +
            "Respond with ONLY the raw JSON object { \"answer\": \"...\" }.",
        question,
        query = new
        {
            queryKind = DiscoveryBankQueryCatalog.For(computed.Spec.Kind).Key,
            metric = MetricKey(computed.Spec.Metric),
            aggregate = computed.Spec.Aggregate.ToString().ToLowerInvariant(),
            computed.Spec.Limit,
            filter = computed.Spec.Filter,
        },
        matchedCount = computed.MatchedCount,
        aggregateValue = computed.AggregateValue,
        rows = computed.Rows.Select(row => new
        {
            row.AuthorHandle,
            row.Title,
            row.NetworkKey,
            row.ContentUrl,
            row.PublishedAtUtc,
            row.MetricValue,
            row.Views,
            row.Likes,
            row.Comments,
            row.Shares,
            row.Saves,
        }),
        groups = computed.Groups,
    };

    /// <summary>
    /// Keeps only the most recent turns, each truncated. Cost of one question must not grow with the
    /// length of the session.
    /// </summary>
    private IReadOnlyList<object> TrimHistory(IReadOnlyList<DiscoveryBankChatTurn>? history)
    {
        if (history is null || history.Count == 0 || _options.MaxHistoryTurns <= 0)
        {
            return [];
        }

        return history
            .Skip(Math.Max(0, history.Count - _options.MaxHistoryTurns))
            .Select(turn => (object)new
            {
                role = string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user",
                text = Truncate(turn.Text ?? string.Empty, _options.MaxHistoryEntryLength),
            })
            .ToList();
    }

    private static string Truncate(string value, int maxLength) =>
        maxLength > 0 && value.Length > maxLength ? value[..maxLength] : value;

    // ── AI routing ───────────────────────────────────────────────────────────

    private async Task<Result<IReadOnlyList<AiRoute>>> ResolveAiRoutesAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var route = await _workflowResolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(organizationId, FlowKey, AnalysisTypeKey, StepKey, OperationKey, null),
            cancellationToken);

        if (route.IsFailure)
        {
            return Result.Failure<IReadOnlyList<AiRoute>>(route.Error);
        }

        var candidates = route.Value
            .Where(candidate => candidate.IsEnabled)
            .OrderBy(candidate => candidate.Priority)
            .Select(candidate => new AiRoute(candidate.ProviderKey, candidate.ModelId))
            .ToList();

        return candidates.Count == 0
            ? Result.Failure<IReadOnlyList<AiRoute>>(IntegrationErrors.AiRouteNotConfigured)
            : Result.Success<IReadOnlyList<AiRoute>>(candidates);
    }

    /// <summary>Tries each configured route in priority order; the last error stands if all of them fail.</summary>
    private async Task<Result<TResponse>> CompleteAsync<TResponse>(
        Guid organizationId,
        string capabilityKey,
        string promptVersionKey,
        string inputJson,
        IReadOnlyList<AiRoute> routes,
        CancellationToken cancellationToken)
        where TResponse : notnull
    {
        var last = Result.Failure<TResponse>(IntegrationErrors.AiRouteNotConfigured);

        foreach (var route in routes)
        {
            Result<AiJsonCompletionResponse<TResponse>> completion;
            try
            {
                completion = await _aiGateway.CompleteJsonAsync<TResponse>(
                    new AiJsonCompletionRequest(
                        organizationId,
                        AnalysisId: null,
                        capabilityKey,
                        promptVersionKey,
                        inputJson,
                        route.ProviderKey,
                        route.ModelId),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "AI Analysis: provider {ProviderKey} threw for org {OrganizationId}.", route.ProviderKey, organizationId);
                last = Result.Failure<TResponse>(IntegrationErrors.AiJsonInvalid);
                continue;
            }

            if (completion.IsSuccess)
            {
                return Result.Success(completion.Value.Payload);
            }

            last = Result.Failure<TResponse>(completion.Error);
        }

        return last;
    }

    private sealed record AiRoute(string ProviderKey, string? ModelId);
}
