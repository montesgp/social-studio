using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Application.Features.Social.DiscoveryBankIntelligence;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.DiscoveryBankIntelligence;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.Tests.Application.Social.DiscoveryBankIntelligence;

/// <summary>
/// The orchestration contract of the AI Analysis tool: the model PLANS and NARRATES, the system
/// COMPUTES. These tests pin the three properties that make the tool trustworthy —
///
/// 1. the model only ever sees the rows a deterministic query returned, capped;
/// 2. an unsupported question executes nothing and says so, naming what the tool CAN answer;
/// 3. an empty bank is a first-class explanation, never an error and never an AI call.
/// </summary>
public sealed class AskDiscoveryBankQuestionHandlerTests
{
    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ── test doubles ─────────────────────────────────────────────────────────

    private sealed class FakeBankReader : IDiscoveryBankReader
    {
        private readonly IReadOnlyList<DiscoveryBankRow> _rows;

        public FakeBankReader(IReadOnlyList<DiscoveryBankRow> rows) => _rows = rows;

        public Task<IReadOnlyList<DiscoveryBankRow>> ListAsync(
            Guid organizationId,
            CancellationToken ct = default) =>
            Task.FromResult(_rows);
    }

    private sealed class FakeAiGateway : IAiCompletionGateway
    {
        private readonly DiscoveryBankQueryPlan? _plan;
        private readonly string _narration;

        public FakeAiGateway(DiscoveryBankQueryPlan? plan, string narration = "Here are the results.")
        {
            _plan = plan;
            _narration = narration;
        }

        public List<AiJsonCompletionRequest> Requests { get; } = [];

        public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
            AiJsonCompletionRequest request,
            CancellationToken cancellationToken = default)
            where TResponse : notnull
        {
            Requests.Add(request);

            if (typeof(TResponse) == typeof(DiscoveryBankQueryPlan))
            {
                if (_plan is null)
                {
                    return Task.FromResult(Result.Failure<AiJsonCompletionResponse<TResponse>>(
                        IntegrationErrors.AiJsonInvalid));
                }

                return Task.FromResult(Result.Success(new AiJsonCompletionResponse<TResponse>(
                    (TResponse)(object)_plan, "claude", "test-model", [])));
            }

            if (typeof(TResponse) == typeof(DiscoveryBankNarration))
            {
                return Task.FromResult(Result.Success(new AiJsonCompletionResponse<TResponse>(
                    (TResponse)(object)new DiscoveryBankNarration(_narration), "claude", "test-model", [])));
            }

            throw new InvalidOperationException($"Unexpected response type {typeof(TResponse).Name}.");
        }
    }

    private sealed class FakeWorkflowResolver : IWorkflowConfigurationResolver
    {
        private readonly bool _configured;

        public FakeWorkflowResolver(bool configured = true) => _configured = configured;

        public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
            WorkflowStepResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!_configured)
            {
                return Task.FromResult(
                    Result.Failure<IReadOnlyList<WorkflowStepRouteCandidate>>(IntegrationErrors.AiRouteNotConfigured));
            }

            IReadOnlyList<WorkflowStepRouteCandidate> candidates =
            [
                new(Guid.NewGuid(), "claude", Guid.NewGuid(), IntegrationConnectionMode.BringYourOwnKey, 1, "test-model", null, true)
            ];

            return Task.FromResult(Result.Success(candidates));
        }
    }

    private static DiscoveryBankRow Row(string key, long views) => new(
        CandidateKey: key,
        NetworkKey: "tiktok",
        Hashtags: ["fitness"],
        ContentUrl: $"https://example.test/{key}",
        AuthorHandle: $"@{key}",
        Title: $"Title {key}",
        PublishedAtUtc: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        ObservedAtUtc: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
        Views: views,
        Likes: null,
        Comments: null,
        Shares: null,
        Saves: null,
        OpportunityScore: null);

    private static AskDiscoveryBankQuestionHandler Handler(
        IReadOnlyList<DiscoveryBankRow> rows,
        FakeAiGateway gateway,
        DiscoveryBankIntelligenceOptions? options = null,
        bool aiConfigured = true) =>
        new(
            new FakeBankReader(rows),
            gateway,
            new FakeWorkflowResolver(aiConfigured),
            Microsoft.Extensions.Options.Options.Create(options ?? new DiscoveryBankIntelligenceOptions()));

    private static DiscoveryBankQueryPlan TopViewsPlan(int? limit = 5) =>
        new("top_by_metric", "views", null, limit, null, null, null, null, null, null, "Top candidates by views");

    // ── the row cap ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NeverSendsTheModelMoreRowsThanTheConfiguredCap()
    {
        var rows = Enumerable.Range(0, 500).Select(i => Row($"k{i:000}", i)).ToArray();
        var gateway = new FakeAiGateway(TopViewsPlan(limit: 500));
        var handler = Handler(rows, gateway, new DiscoveryBankIntelligenceOptions { MaxRowsToModel = 3 });

        var result = await handler.Handle(
            new AskDiscoveryBankQuestionQuery(OrgId, "top 500 by views", []),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Citations.Count);

        var narrationRequest = gateway.Requests.Last();
        using var document = JsonDocument.Parse(narrationRequest.InputJson);
        Assert.Equal(3, document.RootElement.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task TheNarrationCallCarriesTheComputedRows_NotTheWholeBank()
    {
        var rows = Enumerable.Range(0, 50).Select(i => Row($"k{i:000}", i)).ToArray();
        var gateway = new FakeAiGateway(TopViewsPlan(limit: 2));
        var handler = Handler(rows, gateway);

        await handler.Handle(new AskDiscoveryBankQuestionQuery(OrgId, "top 2 by views", []), CancellationToken.None);

        var planRequest = gateway.Requests[0];
        var narrationRequest = gateway.Requests[1];

        // The PLAN call must not carry candidate rows at all — it only maps the question.
        Assert.DoesNotContain("https://example.test/", planRequest.InputJson, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(narrationRequest.InputJson);
        Assert.Equal(2, document.RootElement.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task TrimsTheConversationHistorySentToTheModel()
    {
        var gateway = new FakeAiGateway(TopViewsPlan());
        var handler = Handler([Row("a", 10)], gateway, new DiscoveryBankIntelligenceOptions { MaxHistoryTurns = 2 });

        var history = Enumerable.Range(0, 20)
            .Select(i => new DiscoveryBankChatTurn(i % 2 == 0 ? "user" : "assistant", $"turn-{i}"))
            .ToArray();

        await handler.Handle(
            new AskDiscoveryBankQuestionQuery(OrgId, "top 5 by views", history),
            CancellationToken.None);

        using var document = JsonDocument.Parse(gateway.Requests[0].InputJson);
        Assert.Equal(2, document.RootElement.GetProperty("history").GetArrayLength());
        Assert.Contains("turn-19", gateway.Requests[0].InputJson, StringComparison.Ordinal);
        Assert.DoesNotContain("turn-0\"", gateway.Requests[0].InputJson, StringComparison.Ordinal);
    }

    // ── computed, not guessed ────────────────────────────────────────────────

    [Fact]
    public async Task RanksWithTheSystem_SoTheCitationsAreTheRealTopCandidates()
    {
        var rows = new[] { Row("low", 5), Row("high", 9_000), Row("mid", 100) };
        var gateway = new FakeAiGateway(TopViewsPlan(limit: 2));
        var handler = Handler(rows, gateway);

        var result = await handler.Handle(
            new AskDiscoveryBankQuestionQuery(OrgId, "top 2 by views", []),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DiscoveryBankAnswerStatus.Answered, result.Value.Status);
        Assert.Equal(["high", "mid"], result.Value.Citations.Select(x => x.CandidateKey));
        Assert.All(result.Value.Citations, citation =>
        {
            Assert.StartsWith("https://", citation.ContentUrl, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(citation.AuthorHandle));
        });
    }

    [Fact]
    public async Task ReportsWhichQueryAnsweredTheQuestion()
    {
        var gateway = new FakeAiGateway(TopViewsPlan(limit: 5));
        var handler = Handler([Row("a", 10)], gateway);

        var result = await handler.Handle(
            new AskDiscoveryBankQuestionQuery(OrgId, "top 5 by views", []),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var executed = Assert.IsType<DiscoveryBankExecutedQueryResponse>(result.Value.ExecutedQuery);
        Assert.Equal("top_by_metric", executed.QueryKey);
        Assert.Equal("views", executed.Metric);
        Assert.Equal(5, executed.Limit);
    }

    // ── refusing to improvise ────────────────────────────────────────────────

    [Fact]
    public async Task AnUnsupportedQueryKindExecutesNothingAndNamesWhatTheToolCanAnswer()
    {
        var gateway = new FakeAiGateway(new DiscoveryBankQueryPlan(
            "rank_by_vibes", "views", null, 5, null, null, null, null, null, null, null));
        var handler = Handler([Row("a", 10)], gateway);

        var result = await handler.Handle(
            new AskDiscoveryBankQuestionQuery(OrgId, "which of these will go viral next week?", []),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DiscoveryBankAnswerStatus.Unsupported, result.Value.Status);
        Assert.Null(result.Value.ExecutedQuery);
        Assert.Empty(result.Value.Citations);
        Assert.NotEmpty(result.Value.SupportedQueries);

        // Exactly one AI call: the plan. No narration over data that was never computed.
        Assert.Single(gateway.Requests);
    }

    [Fact]
    public async Task AnOutOfDomainParameterExecutesNothing()
    {
        var gateway = new FakeAiGateway(new DiscoveryBankQueryPlan(
            "top_by_metric", "views", null, 5, "myspace", null, null, null, null, null, null));
        var handler = Handler([Row("a", 10)], gateway);

        var result = await handler.Handle(
            new AskDiscoveryBankQuestionQuery(OrgId, "top 5 on myspace", []),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DiscoveryBankAnswerStatus.Unsupported, result.Value.Status);
        Assert.Single(gateway.Requests);
    }

    // ── empty bank ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AnEmptyBankIsExplained_NotFailed_AndCostsNoAiCall()
    {
        var gateway = new FakeAiGateway(TopViewsPlan());
        var handler = Handler([], gateway);

        var result = await handler.Handle(
            new AskDiscoveryBankQuestionQuery(OrgId, "top 5 by views", []),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DiscoveryBankAnswerStatus.BankEmpty, result.Value.Status);
        Assert.Contains("Single Analyzer", result.Value.Answer, StringComparison.Ordinal);
        Assert.Empty(gateway.Requests);
    }

    // ── guards ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RejectsAnEmptyQuestion()
    {
        var handler = Handler([Row("a", 10)], new FakeAiGateway(TopViewsPlan()));

        var result = await handler.Handle(
            new AskDiscoveryBankQuestionQuery(OrgId, "   ", []),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DiscoveryBankErrors.QuestionRequired.Code, result.Error.Code);
    }

    [Fact]
    public async Task RejectsAQuestionLongerThanTheConfiguredCap()
    {
        var handler = Handler(
            [Row("a", 10)],
            new FakeAiGateway(TopViewsPlan()),
            new DiscoveryBankIntelligenceOptions { MaxQuestionLength = 20 });

        var result = await handler.Handle(
            new AskDiscoveryBankQuestionQuery(OrgId, new string('x', 21), []),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DiscoveryBankErrors.QuestionTooLong.Code, result.Error.Code);
    }

    [Fact]
    public async Task FailsClearlyWhenNoAiRouteIsConfigured()
    {
        var handler = Handler([Row("a", 10)], new FakeAiGateway(TopViewsPlan()), aiConfigured: false);

        var result = await handler.Handle(
            new AskDiscoveryBankQuestionQuery(OrgId, "top 5 by views", []),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.AiRouteNotConfigured.Code, result.Error.Code);
    }

    [Fact]
    public async Task APlanCallThatFailsSurfacesAsAFailure_NeverAsAGuessedAnswer()
    {
        var handler = Handler([Row("a", 10)], new FakeAiGateway(plan: null));

        var result = await handler.Handle(
            new AskDiscoveryBankQuestionQuery(OrgId, "top 5 by views", []),
            CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}
