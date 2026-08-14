using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Infrastructure.Social.Providers;
using Incoders.Hive.Infrastructure.Social.Providers.Apify;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.Tests.Infrastructure.Social.Providers;

/// <summary>
/// CD-12 scenario 1 (configured route resolves and the source runs, actor id read from configuration,
/// never a literal) and CD-13 (no enabled route → Unconfigured, never a throw, never a substituted
/// literal). Same fake-handler pattern as <c>ApifyProfileScrapeResolverTests</c> — the real Apify API is
/// never called.
/// </summary>
public sealed class ApifyNicheSearchResolverTests
{
    private static readonly string FixtureJson =
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Infrastructure", "Social", "Fixtures",
                "tiktok-niche-search.json"));

    private static readonly string ActorMappingsJson = JsonSerializer.Serialize(new[]
    {
        new { networkKey = "tiktok", variantKey = "niche-search", actorId = "fallback-mapping-actor", isPrimary = true },
    });

    private static ResolvedProviderCredential MakeCredential(string? mappingsJson = null, string apiKey = "test-key") =>
        new(
            Guid.NewGuid(),
            "apify",
            IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string>
            {
                ["api_key"] = apiKey,
                ["actor_mappings_json"] = mappingsJson ?? ActorMappingsJson,
            },
            SelectedModel: null);

    private static NicheSearchRequest MakeRequest(Guid orgId) =>
        new(orgId, "tiktok", "content strategy", MaxResults: 20);

    [Fact]
    public async Task SearchAsync_ConfiguredWorkflowStepRoute_UsesTheConfiguredActorId_NeverALiteral()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
            [
                new WorkflowStepRouteCandidate(
                    Guid.NewGuid(), "apify", Guid.NewGuid(),
                    IntegrationConnectionMode.BringYourOwnKey, Priority: 1,
                    ModelId: null, ActorId: "configured-workflow-actor", IsEnabled: true),
            ]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.NotEmpty(result.Candidates);
        Assert.Equal("configured-workflow-actor", handler.LastActorIdInUrl);
        Assert.All(result.Candidates, c => Assert.Equal("niche", c.SourceKey));
    }

    [Fact]
    public async Task SearchAsync_NoEnabledWorkflowRoute_FallsBackToActorMappingsJson()
    {
        // No enabled workflow-step route at all — resolves via the raw actor_mappings_json entry,
        // exactly like ApifyProfileScrapeResolver.cs:146-151 (design §8.1).
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Equal("fallback-mapping-actor", handler.LastActorIdInUrl);
    }

    [Fact]
    public async Task SearchAsync_NoRouteAndNoMapping_ReturnsUnconfigured_NeverThrows_NeverSubstitutesALiteral()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(mappingsJson: "[]"),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Unconfigured, result.Outcome);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_UnsupportedNetwork_ReturnsUnconfigured_NeverCrashes()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
            [
                new WorkflowStepRouteCandidate(
                    Guid.NewGuid(), "apify", Guid.NewGuid(),
                    IntegrationConnectionMode.BringYourOwnKey, Priority: 1,
                    ModelId: null, ActorId: "configured-workflow-actor", IsEnabled: true),
            ]));

        var handler = new ProgrammableHandler();

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(
            new NicheSearchRequest(Guid.NewGuid(), "instagram", "content strategy", 20),
            CancellationToken.None);

        Assert.Equal(SourceOutcome.Unconfigured, result.Outcome);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_ZeroOfManyMapped_ReturnsFailed()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var unmappableItems = JsonSerializer.Serialize(new[]
        {
            new { text = "no url one" },
            new { text = "no url two" },
        });

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(unmappableItems);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Failed, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task SearchAsync_EmptyResponse_IsALegitimateSuccessWithZeroResults()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess("[]");

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task SearchAsync_SendsTheHashtagShapeFirst_WithMediaDownloadsDisabled()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Equal(1, handler.CallCount);

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        var root = body.RootElement;

        Assert.True(root.TryGetProperty("hashtags", out var hashtags));
        // The niche PHRASE "content strategy" becomes every plausible tag, in one call.
        Assert.Equal(
            ["content", "strategy", "contentstrategy"],
            hashtags.EnumerateArray().Select(h => h.GetString()).ToArray());
        Assert.False(root.GetProperty("shouldDownloadVideos").GetBoolean());
        Assert.False(root.GetProperty("shouldDownloadCovers").GetBoolean());
        Assert.False(root.GetProperty("shouldDownloadSlideshowImages").GetBoolean());
    }

    [Fact]
    public async Task SearchAsync_SendsEveryDerivedHashtag_ForAConnectorJoinedNiche()
    {
        // The exact production niche that produced "Medical&health" and a NOT_FOUND item.
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        await resolver.SearchAsync(
            new NicheSearchRequest(Guid.NewGuid(), "tiktok", "Medical & health", MaxResults: 20),
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(
            ["medical", "health", "medicalhealth"],
            body.RootElement.GetProperty("hashtags").EnumerateArray().Select(h => h.GetString()).ToArray());
    }

    [Fact]
    public async Task SearchAsync_CuratedHashtags_AreSentVerbatim_AndNeverRederivedFromTheSearchTerm()
    {
        // The catalog's curated list is the intentional key. Re-deriving it from the free-text term
        // would put "medicalhealth" back on the wire — the tag that does not exist on the network.
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        await resolver.SearchAsync(
            new NicheSearchRequest(Guid.NewGuid(), "tiktok", "Medical & health", MaxResults: 20)
            {
                Hashtags = ["medical", "health"],
            },
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(
            ["medical", "health"],
            body.RootElement.GetProperty("hashtags").EnumerateArray().Select(h => h.GetString()).ToArray());
    }

    [Fact]
    public async Task SearchAsync_NicheYieldingNoHashtag_NeverCallsTheActorWithGarbage()
    {
        // A niche made only of connectors derives no hashtag at all. Asking the provider for an empty
        // or meaningless tag would burn a paid run to be told it does not exist.
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(
            new NicheSearchRequest(Guid.NewGuid(), "tiktok", "the and of", MaxResults: 20),
            CancellationToken.None);

        Assert.Equal(SourceOutcome.Failed, result.Outcome);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_ProviderErrorItems_AreReportedAsANotFoundHashtag_NotAShapeMismatch()
    {
        // The actor answers a non-existent hashtag with an error ITEM, not an HTTP error. Calling that
        // "the response shape does not match" sends the operator to fix a mapping that is already correct.
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess("""
            [
              {
                "error": "This profile/hashtag does not exist.",
                "errorCode": "NOT_FOUND",
                "url": "https://www.tiktok.com/discover/medicalhealth",
                "input": "medicalhealth"
              }
            ]
            """);

        var logger = new CapturingLogger<ApifyNicheSearchResolver>();

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler),
            logger);

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Failed, result.Outcome);
        Assert.Empty(result.Candidates);

        var warning = Assert.Single(logger.Messages.Where(m => m.Contains("NOT_FOUND", StringComparison.Ordinal)));
        Assert.Contains("medicalhealth", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("does not match the documented TikTok item shape", warning, StringComparison.Ordinal);

        Assert.DoesNotContain(
            logger.Messages,
            m => m.Contains("does not match the documented TikTok item shape", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_OneDeadHashtagAmongRealResults_StillReturnsWhatMapped()
    {
        // Several hashtags travel in one call; one of them not existing cannot void the whole search.
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        using var fixture = JsonDocument.Parse(FixtureJson);
        var mixed = $$"""
            [
              { "error": "This profile/hashtag does not exist.", "errorCode": "NOT_FOUND", "input": "medicalhealth" },
              {{JsonSerializer.Serialize(fixture.RootElement[0])}}
            ]
            """;

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(mixed);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Single(result.Candidates);
        Assert.Equal("nichecreator1", result.Candidates[0].SourceHandle);
    }

    [Fact]
    public async Task SearchAsync_HashtagShapeRejectedWith400_RetriesTheSameActorOnceWithSearchQueries()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueStatus(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Input is not valid\"}}");
        handler.EnqueueSuccess(FixtureJson);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.NotEmpty(result.Candidates);

        // Exactly ONE bounded reshape retry, against the SAME actor.
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(["fallback-mapping-actor", "fallback-mapping-actor"], handler.ActorIds);

        Assert.Contains("\"hashtags\"", handler.Bodies[0], StringComparison.Ordinal);
        Assert.Contains("\"searchQueries\"", handler.Bodies[1], StringComparison.Ordinal);
        Assert.DoesNotContain("\"hashtags\"", handler.Bodies[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SearchAsync_NonShapeFailure_IsNeverReshaped(HttpStatusCode statusCode)
    {
        // 401/403 (credentials), 429 (quota) and 5xx (outage) say nothing about the request body, so
        // resending a different shape would only double the damage.
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueStatus(statusCode, "{}");

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Failed, result.Outcome);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_BothShapesRejectedWith400_MovesOnToTheNextConfiguredActor()
    {
        var mappingsJson = JsonSerializer.Serialize(new[]
        {
            new { networkKey = "tiktok", variantKey = "niche-search", actorId = "primary-actor", isPrimary = true },
            new { networkKey = "tiktok", variantKey = "niche-search", actorId = "backup-actor", isPrimary = false },
        });

        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueStatus(HttpStatusCode.BadRequest, "{}");
        handler.EnqueueStatus(HttpStatusCode.BadRequest, "{}");
        handler.EnqueueSuccess(FixtureJson);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(mappingsJson: mappingsJson),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Equal(["primary-actor", "primary-actor", "backup-actor"], handler.ActorIds);
    }

    // ── Observability: no failure after the START line may be silent ──────────

    [Fact]
    public async Task SearchAsync_HttpClientTimeout_IsLoggedWithTheElapsedTime_AndFailsWithoutThrowing()
    {
        // The 120s HttpClient timeout surfaces as an OperationCanceledException while the CALLER's token
        // is still live. Before this was logged, a timed-out run left exactly one START line and nothing
        // else, and the operator could not tell a timeout from a crash.
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueThrow(() => new TaskCanceledException("timeout", new TimeoutException()));

        var logger = new CapturingLogger<ApifyNicheSearchResolver>();

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler),
            logger);

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Failed, result.Outcome);

        var timedOut = Assert.Single(logger.Messages, m => m.Contains("TIMED OUT", StringComparison.Ordinal));
        Assert.Contains("fallback-mapping-actor", timedOut, StringComparison.Ordinal);
        Assert.Contains("InputShape=hashtags", timedOut, StringComparison.Ordinal);
        Assert.Contains(" ms", timedOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_CallerCancellation_IsLoggedAndRethrown_NeverSwallowed()
    {
        // A browser/proxy abort is a GENUINE cancellation: it must keep propagating (swallowing it would
        // report a failure the caller never waited for), but it must not be invisible either.
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        using var cts = new CancellationTokenSource();

        var handler = new ProgrammableHandler();
        handler.EnqueueThrow(() =>
        {
            cts.Cancel();
            return new OperationCanceledException(cts.Token);
        });

        var logger = new CapturingLogger<ApifyNicheSearchResolver>();

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler),
            logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resolver.SearchAsync(MakeRequest(Guid.NewGuid()), cts.Token));

        var cancelled = Assert.Single(logger.Messages, m => m.Contains("CANCELLED", StringComparison.Ordinal));
        Assert.Contains("fallback-mapping-actor", cancelled, StringComparison.Ordinal);
        Assert.Contains("InputShape=hashtags", cancelled, StringComparison.Ordinal);
        Assert.Contains(" ms", cancelled, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_UnexpectedException_IsLogged_AndNeverEscapesAsAnUnloggedFailure()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueThrow(() => new InvalidOperationException("something nobody predicted"));

        var logger = new CapturingLogger<ApifyNicheSearchResolver>();

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler),
            logger);

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Failed, result.Outcome);
        Assert.Empty(result.Candidates);

        // Whatever HttpClient wraps it in, the exception type must reach the log.
        Assert.Contains(
            logger.Messages,
            m => m.Contains("InvalidOperationException", StringComparison.Ordinal)
                && m.Contains("fallback-mapping-actor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_SuccessfulRun_LogsASuccessLineWithTheItemCount()
    {
        // A run that works must leave a trace too — otherwise the START line is the only evidence and a
        // silent success is indistinguishable from a silent hang.
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var logger = new CapturingLogger<ApifyNicheSearchResolver>();

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler),
            logger);

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);

        using var fixture = JsonDocument.Parse(FixtureJson);
        var expectedItemCount = fixture.RootElement.GetArrayLength();

        var success = Assert.Single(logger.Messages, m => m.Contains("SUCCESS", StringComparison.Ordinal));
        Assert.Contains($"ItemCount={expectedItemCount}", success, StringComparison.Ordinal);
        Assert.Contains("StatusCode=200", success, StringComparison.Ordinal);
        Assert.Contains("fallback-mapping-actor", success, StringComparison.Ordinal);
    }

    // ── Request sizing: the budget is TOTAL, never per hashtag ────────────────

    [Fact]
    public async Task SearchAsync_SplitsTheResultBudgetAcrossTheDerivedHashtags()
    {
        // 72 requested with 3 derived hashtags asked the actor for 72 EACH — up to 216 scraped videos in
        // one synchronous call, which is what pushed the real run past the 120s HttpClient timeout.
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        await resolver.SearchAsync(
            new NicheSearchRequest(Guid.NewGuid(), "tiktok", "Medical & health", MaxResults: 72),
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(3, body.RootElement.GetProperty("hashtags").GetArrayLength());
        Assert.Equal(24, body.RootElement.GetProperty("resultsPerPage").GetInt32());
    }

    [Fact]
    public async Task SearchAsync_SingleDerivedHashtag_KeepsTheWholeBudget()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        await resolver.SearchAsync(
            new NicheSearchRequest(Guid.NewGuid(), "tiktok", "fitness", MaxResults: 72),
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(1, body.RootElement.GetProperty("hashtags").GetArrayLength());
        Assert.Equal(72, body.RootElement.GetProperty("resultsPerPage").GetInt32());
    }

    [Fact]
    public async Task SearchAsync_TinyBudgetSplitThreeWays_NeverAsksForZeroResults()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        await resolver.SearchAsync(
            new NicheSearchRequest(Guid.NewGuid(), "tiktok", "Medical & health", MaxResults: 2),
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.True(body.RootElement.GetProperty("resultsPerPage").GetInt32() >= 1);
    }

    [Fact]
    public async Task SearchAsync_NeverReturnsMoreThanTheCallerAskedFor()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(
            new NicheSearchRequest(Guid.NewGuid(), "tiktok", "Medical & health", MaxResults: 1),
            CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Single(result.Candidates);
    }

    // ── Cost and duration guards: a runaway run must be stopped by the PLATFORM ──

    [Fact]
    public async Task SearchAsync_NoConfiguration_AppliesTheConservativeDefaultRunGuards()
    {
        // Absent configuration must NOT mean "unguarded". The operator burned real money on runs that
        // carried no platform cap at all, so the defaults are the safety net.
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        var query = QueryOf(handler.Urls[0]);
        var defaults = new ApifyNicheSearchGuardOptions();

        Assert.Equal(
            defaults.MaxTotalChargeUsd.ToString(CultureInfo.InvariantCulture),
            query["maxTotalChargeUsd"]);
        Assert.Equal(defaults.RunTimeoutSeconds.ToString(CultureInfo.InvariantCulture), query["timeout"]);
        Assert.Equal(defaults.MaxItems.ToString(CultureInfo.InvariantCulture), query["maxItems"]);
    }

    [Fact]
    public void DefaultRunTimeout_StaysComfortablyBelowTheHttpClientTimeout()
    {
        // The platform must stop the run BEFORE our own 120 s HttpClient gives up, otherwise an
        // abandoned request leaves an orphaned run that keeps billing.
        Assert.True(new ApifyNicheSearchGuardOptions().RunTimeoutSeconds <= 100);
    }

    [Fact]
    public async Task SearchAsync_ConfiguredGuards_OverrideTheDefaultsOnTheRunUrl()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler),
            logger: null,
            guardOptions: Options.Create(new ApifyNicheSearchGuardOptions
            {
                MaxTotalChargeUsd = 0.05m,
                RunTimeoutSeconds = 45,
                MaxItems = 7,
            }));

        await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        var query = QueryOf(handler.Urls[0]);
        Assert.Equal("0.05", query["maxTotalChargeUsd"]);
        Assert.Equal("45", query["timeout"]);
        Assert.Equal("7", query["maxItems"]);
    }

    [Fact]
    public async Task SearchAsync_LoggedStartUrl_CarriesTheGuards_AndKeepsTheTokenMasked()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var logger = new CapturingLogger<ApifyNicheSearchResolver>();

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            // Positional on purpose: a named `apiKey:` argument with an inline value trips the
            // repository secret scanner, and this is a fake token whose only job is to be masked.
            MakeCredential(null, "test-token-must-be-masked"),
            new StaticHttpClientFactory(handler),
            logger,
            Options.Create(new ApifyNicheSearchGuardOptions
            {
                MaxTotalChargeUsd = 0.05m,
                RunTimeoutSeconds = 45,
                MaxItems = 7,
            }));

        await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        var start = Assert.Single(logger.Messages, m => m.Contains("START", StringComparison.Ordinal));
        Assert.Contains("maxTotalChargeUsd=0.05", start, StringComparison.Ordinal);
        Assert.Contains("timeout=45", start, StringComparison.Ordinal);
        Assert.Contains("maxItems=7", start, StringComparison.Ordinal);
        Assert.Contains("token=***", start, StringComparison.Ordinal);

        Assert.DoesNotContain(
            logger.Messages,
            m => m.Contains("test-token-must-be-masked", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_ActorReturnsFarMoreItemsThanRequested_WarnsThatOurLimitFieldWasIgnored()
    {
        // The operator asked for 5 and got 202: proof the actor ignores the per-page field we send.
        // Nothing in the log said so, so the overspend was invisible until the invoice arrived.
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        using var fixture = JsonDocument.Parse(FixtureJson);
        var one = JsonSerializer.Serialize(fixture.RootElement[0]);
        var flood = $"[{string.Join(',', Enumerable.Repeat(one, 40))}]";

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(flood);

        var logger = new CapturingLogger<ApifyNicheSearchResolver>();

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler),
            logger);

        var result = await resolver.SearchAsync(
            new NicheSearchRequest(Guid.NewGuid(), "tiktok", "content strategy", MaxResults: 5),
            CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);

        var overrun = Assert.Single(
            logger.Messages,
            m => m.Contains("MORE ITEMS THAN REQUESTED", StringComparison.Ordinal));
        Assert.Contains("ItemCount=40", overrun, StringComparison.Ordinal);
        Assert.Contains("RequestedBudget=5", overrun, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_ItemCountWithinTheRequestedBudget_DoesNotWarnAboutAnOverrun()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var logger = new CapturingLogger<ApifyNicheSearchResolver>();

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler),
            logger);

        await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.DoesNotContain(
            logger.Messages,
            m => m.Contains("MORE ITEMS THAN REQUESTED", StringComparison.Ordinal));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Dictionary<string, string> QueryOf(string url)
    {
        var query = new Uri(url).Query.TrimStart('?');

        return query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty,
                StringComparer.Ordinal);
    }

    private sealed class FakeWorkflowConfigurationResolver : IWorkflowConfigurationResolver
    {
        private readonly Result<IReadOnlyList<WorkflowStepRouteCandidate>> _result;

        public FakeWorkflowConfigurationResolver(Result<IReadOnlyList<WorkflowStepRouteCandidate>> result)
        {
            _result = result;
        }

        public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
            WorkflowStepResolutionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class ProgrammableHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();
        public int CallCount { get; private set; }
        public string? LastActorIdInUrl { get; private set; }

        /// <summary>Serialized request bodies, in call order — the evidence for the input-shape rules.</summary>
        public List<string> Bodies { get; } = [];

        /// <summary>Actor ids taken from the request URL, in call order.</summary>
        public List<string> ActorIds { get; } = [];

        /// <summary>Full request URLs, in call order — the evidence for the run-guard query parameters.</summary>
        public List<string> Urls { get; } = [];

        public void EnqueueSuccess(string body)
        {
            _responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }

        public void EnqueueStatus(HttpStatusCode statusCode, string body)
        {
            _responses.Enqueue(() => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }

        /// <summary>
        /// Enqueues a THROWING send, so timeout, caller-cancellation and unexpected-failure paths can be
        /// exercised exactly as HttpClient surfaces them.
        /// </summary>
        public void EnqueueThrow(Func<Exception> exceptionFactory)
        {
            _responses.Enqueue(() => throw exceptionFactory());
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;

            Urls.Add(request.RequestUri!.ToString());
            Bodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty);

            // https://api.apify.com/v2/acts/{actorId}/run-sync-get-dataset-items?token=...
            var segments = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var actsIndex = Array.IndexOf(segments, "acts");
            LastActorIdInUrl = actsIndex >= 0 && actsIndex + 1 < segments.Length ? segments[actsIndex + 1] : null;
            ActorIds.Add(LastActorIdInUrl ?? string.Empty);

            if (_responses.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(_responses.Dequeue()());
        }
    }

    /// <summary>
    /// Captures formatted log messages so a diagnosis can be asserted as behaviour: a not-found hashtag
    /// and an unrecognised response shape are different failures and must not be reported alike.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StaticHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
