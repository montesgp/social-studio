using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.Semantics;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Tests.Application.Features.Social.NicheDiscovery.Semantics;

/// <summary>
/// WU-10 (design §8): gateway success path uses <see cref="IAiCompletionGateway.CompleteJsonAsync{TResponse}"/>
/// with <c>CapabilityKey = "ai-analysis"</c>; the five prompt version keys from design §8 are reserved as
/// named constants and the consolidated cluster call is the one actually issued today (flagged deviation
/// — see <see cref="SemanticAnnotator"/>'s class doc). Within-run memoisation by SHA-256 of the canonical
/// JSON payload (N10) so an identical input is never re-annotated twice in one run.
/// </summary>
public sealed class SemanticAnnotatorTests
{
    private static readonly Guid Org = Guid.NewGuid();

    [Fact]
    public async Task AnnotateAsync_EmptyCandidates_ReturnsEmptyWithoutCallingTheGateway()
    {
        var ai = new FakeGateway(Success());
        var annotator = new SemanticAnnotator(ai);

        var outcome = await annotator.AnnotateAsync(Org, [], CancellationToken.None);

        Assert.Empty(outcome.Annotation.Clusters);
        Assert.False(outcome.Degraded);
        Assert.False(ai.WasCalled);
    }

    [Fact]
    public async Task AnnotateAsync_Success_UsesTheAiAnalysisCapabilityKey()
    {
        var ai = new FakeGateway(Success());
        var annotator = new SemanticAnnotator(ai);

        await annotator.AnnotateAsync(Org, [Candidate("v1", "chan-1")], CancellationToken.None);

        Assert.Equal("ai-analysis", ai.LastCapabilityKey);
    }

    [Fact]
    public async Task AnnotateAsync_Success_TheFivePromptVersionKeysFromDesignAreDefined()
    {
        // Reserved for the staged classify/cluster/name/pattern/topic pipeline (design §8) — today's
        // consolidated call uses ClusterPromptVersionKey; the other four exist as named constants so a
        // later staged decomposition is a wiring change, not a new vocabulary.
        Assert.Equal("niche-discovery.classify.v1", SemanticAnnotator.ClassifyPromptVersionKey);
        Assert.Equal("niche-discovery.cluster.v1", SemanticAnnotator.ClusterPromptVersionKey);
        Assert.Equal("niche-discovery.name-cluster.v1", SemanticAnnotator.NameClusterPromptVersionKey);
        Assert.Equal("niche-discovery.extract-pattern.v1", SemanticAnnotator.ExtractPatternPromptVersionKey);
        Assert.Equal("niche-discovery.identify-topic.v1", SemanticAnnotator.IdentifyTopicPromptVersionKey);
    }

    [Fact]
    public async Task AnnotateAsync_Success_ReturnsTheGatewayPayloadNotDegraded()
    {
        var payload = new SemanticAnnotation(
            [new ClusterProposal("budget travel", "Listicle", ["v1"])],
            ["backpacking"],
            ["TalkingHead"]);
        var ai = new FakeGateway(payload);
        var annotator = new SemanticAnnotator(ai);

        var outcome = await annotator.AnnotateAsync(Org, [Candidate("v1", "chan-1")], CancellationToken.None);

        Assert.False(outcome.Degraded);
        Assert.Equal(payload, outcome.Annotation);
    }

    [Fact]
    public async Task AnnotateAsync_IdenticalInputPayload_IsMemoisedWithinOneRun_NeverCallsTheGatewayTwice()
    {
        var ai = new FakeGateway(Success());
        var annotator = new SemanticAnnotator(ai);
        var candidates = new[] { Candidate("v1", "chan-1") };

        await annotator.AnnotateAsync(Org, candidates, CancellationToken.None);
        await annotator.AnnotateAsync(Org, candidates, CancellationToken.None);

        Assert.Equal(1, ai.CallCount);
    }

    [Fact]
    public async Task AnnotateAsync_DifferentInputPayload_IsNotMemoised_CallsTheGatewayAgain()
    {
        var ai = new FakeGateway(Success());
        var annotator = new SemanticAnnotator(ai);

        await annotator.AnnotateAsync(Org, [Candidate("v1", "chan-1")], CancellationToken.None);
        await annotator.AnnotateAsync(Org, [Candidate("v2", "chan-2")], CancellationToken.None);

        Assert.Equal(2, ai.CallCount);
    }

    [Fact]
    public async Task AnnotateAsync_GatewayFailure_FallsBackToLexicalClustering_AndReportsDegraded()
    {
        var ai = FakeGateway.Failing();
        var annotator = new SemanticAnnotator(ai);
        var candidates = new[]
        {
            Candidate("v1", "chan-1", "Cómo elegir presupuesto viaje barato", "guía completa presupuesto viaje"),
            Candidate("v2", "chan-2", "Guía presupuesto viaje económico", "tips presupuesto viaje barato"),
            Candidate("v3", "chan-3", "Presupuesto viaje low cost 2026", "consejos presupuesto viaje"),
        };

        var outcome = await annotator.AnnotateAsync(Org, candidates, CancellationToken.None);

        Assert.True(outcome.Degraded);
        Assert.NotEmpty(outcome.Annotation.Clusters);
        // The run still ranks: the fallback still produces something for the deterministic pipeline to
        // consume — never an empty result just because the model was unavailable.
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static SemanticAnnotationCandidate Candidate(
        string externalRef, string channelExternalRef, string title = "Title", string? description = null) =>
        new(externalRef, channelExternalRef, title, description);

    private static SemanticAnnotation Success() => new([], [], []);

    private sealed class FakeGateway : IAiCompletionGateway
    {
        private readonly SemanticAnnotation? _payload;
        private readonly bool _fails;

        public FakeGateway(SemanticAnnotation payload)
        {
            _payload = payload;
        }

        private FakeGateway(bool fails)
        {
            _fails = fails;
        }

        public static FakeGateway Failing() => new(true);

        public bool WasCalled { get; private set; }
        public int CallCount { get; private set; }
        public string? LastCapabilityKey { get; private set; }

        public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
            AiJsonCompletionRequest request,
            CancellationToken cancellationToken = default)
            where TResponse : notnull
        {
            WasCalled = true;
            CallCount++;
            LastCapabilityKey = request.CapabilityKey;

            if (_fails)
            {
                return Task.FromResult(Result.Failure<AiJsonCompletionResponse<TResponse>>(
                    Error.Failure("ai.provider-down", "provider unavailable")));
            }

            var payload = (TResponse)(object)_payload!;

            return Task.FromResult(Result.Success(
                new AiJsonCompletionResponse<TResponse>(payload, "fake", "fake-model", [])));
        }
    }
}
