using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Ideas;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Ideas;

/// <summary>
/// Tests for the model-backed idea source shared by "from the client's strategy" and
/// "from everything we know about the client".
/// </summary>
public sealed class AiClientIdeaSourceTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();

    [Fact]
    public async Task Draw_ReturnsNothingWhenTheGroundingIsEmpty()
    {
        // The defining case: a client with no strategy authored yet.
        var ai = new FakeGateway(Ideas("a", "b", "c"));
        var source = Build(grounding: null, ai);

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 3), CancellationToken.None);

        Assert.Empty(drawn);
        // The model must not even be called: an ungrounded source costs nothing.
        Assert.False(ai.WasCalled);
    }

    [Fact]
    public async Task Draw_ReturnsNothingWhenTheGroundingIsWhitespace()
    {
        var ai = new FakeGateway(Ideas("a"));
        var source = Build(grounding: "   ", ai);

        Assert.Empty(await source.DrawAsync(new ContentIdeaRequest(Org, Client, 3), CancellationToken.None));
        Assert.False(ai.WasCalled);
    }

    [Fact]
    public async Task Draw_MapsModelIdeasToContentIdeas()
    {
        var ai = new FakeGateway(Ideas("Cómo elegir un CRM", "Errores al migrar"));
        var source = Build("estrategia del cliente", ai);

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 5), CancellationToken.None);

        Assert.Equal(2, drawn.Count);
        Assert.Equal("Cómo elegir un CRM", drawn[0].Title);
        Assert.All(drawn, idea => Assert.Equal("client-strategy", idea.SourceKey));
    }

    [Fact]
    public async Task Draw_NeverCarriesASignal()
    {
        var ai = new FakeGateway(Ideas("un tema"));
        var source = Build("grounding", ai);

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 1), CancellationToken.None);

        // An invented topic has no view count. Faking one would let it outrank a genuinely winning
        // post scraped from the market.
        Assert.Null(drawn[0].Signal);
    }

    [Fact]
    public async Task Draw_TrimsToTheRequestedCount()
    {
        var ai = new FakeGateway(Ideas("a", "b", "c", "d", "e"));
        var source = Build("grounding", ai);

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 2), CancellationToken.None);

        // A chatty model must never inflate the batch — each extra idea is a full paid run.
        Assert.Equal(2, drawn.Count);
    }

    [Fact]
    public async Task Draw_DropsBlankAndDuplicateTitles()
    {
        var ai = new FakeGateway([
            new GeneratedIdea("Tema uno", null),
            new GeneratedIdea("   ", null),
            new GeneratedIdea("tema uno", null),
            new GeneratedIdea(null, null),
            new GeneratedIdea("Tema dos", null)
        ]);
        var source = Build("grounding", ai);

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 10), CancellationToken.None);

        // A blank topic would become a piece with no brief at all.
        Assert.Equal(2, drawn.Count);
    }

    [Fact]
    public async Task Draw_ThrowsWhenTheProviderFails()
    {
        var source = Build("grounding", FakeGateway.Failing());

        // Throwing is deliberate: the chain records it in FailedSourceKeys so a short batch has a
        // stated reason, instead of looking like "the client had nothing to say".
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.DrawAsync(new ContentIdeaRequest(Org, Client, 3), CancellationToken.None));
    }

    [Fact]
    public async Task Draw_HandlesAModelReturningNoIdeas()
    {
        var source = Build("grounding", new FakeGateway(null));

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 3), CancellationToken.None);

        Assert.Empty(drawn);
    }

    [Fact]
    public async Task Draw_AsksTheModelForExactlyWhatWasRequested()
    {
        var ai = new FakeGateway(Ideas("a"));
        var source = Build("grounding", ai);

        await source.DrawAsync(new ContentIdeaRequest(Org, Client, 7), CancellationToken.None);

        Assert.Contains("\"count\":7", ai.LastInputJson);
        Assert.Contains("grounding", ai.LastInputJson);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<GeneratedIdea> Ideas(params string[] titles) =>
        titles.Select(t => new GeneratedIdea(t, null)).ToList();

    private static AiClientIdeaSource Build(string? grounding, FakeGateway ai) =>
        new("client-strategy", 1, new FakeGrounding(grounding), ai, "content.ideas", "v1");

    private sealed class FakeGrounding(string? grounding) : IClientIdeaGrounding
    {
        public string GroundingKey => "client-strategy";

        public Task<string?> BuildAsync(Guid organizationId, Guid clientId, CancellationToken ct) =>
            Task.FromResult(grounding);
    }

    private sealed class FakeGateway : IAiCompletionGateway
    {
        private readonly IReadOnlyList<GeneratedIdea>? _ideas;
        private readonly bool _fails;

        public FakeGateway(IReadOnlyList<GeneratedIdea>? ideas)
        {
            _ideas = ideas;
        }

        private FakeGateway(bool fails)
        {
            _fails = fails;
        }

        public static FakeGateway Failing() => new(true);

        public bool WasCalled { get; private set; }
        public string LastInputJson { get; private set; } = string.Empty;

        public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
            AiJsonCompletionRequest request,
            CancellationToken cancellationToken = default)
            where TResponse : notnull
        {
            WasCalled = true;
            LastInputJson = request.InputJson;

            if (_fails)
            {
                return Task.FromResult(Result.Failure<AiJsonCompletionResponse<TResponse>>(
                    Error.Failure("ai.provider-down", "provider unavailable")));
            }

            var payload = (TResponse)(object)new GeneratedIdeaList(_ideas);

            return Task.FromResult(Result.Success(
                new AiJsonCompletionResponse<TResponse>(payload, "fake", "fake-model", [])));
        }
    }
}
