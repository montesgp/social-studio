using System.Net;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Infrastructure.Social.Providers;

namespace Incoders.Hive.Tests.Infrastructure.Social.ProfileAnalysis;

/// <summary>
/// Unit/integration tests for ApifyProfileScrapeResolver.
/// HTTP layer is mocked via a controllable HttpMessageHandler — real Apify API is never called.
/// Covers: PA-04, PA-05, PA-06, PA-23, PA-24, INT-01, INT-02.
/// </summary>
public sealed class ApifyProfileScrapeResolverTests
{
    private static readonly string GoldenFixture =
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Infrastructure", "Social", "Fixtures",
                "apify-instagram-profile-response.json"));

    private static readonly string ActorMappingsJson = JsonSerializer.Serialize(new[]
    {
        new { networkKey = "instagram", variantKey = "profile", actorId = "get-leads~all-in-one-instagram-scraper", isPrimary = true },
        new { networkKey = "instagram", variantKey = "profile", actorId = "apify~instagram-profile-scraper", isPrimary = false }
    });

    private static ProfileScrapeRequest MakeRequest(Guid orgId, Guid analysisId) =>
        new(orgId, analysisId, null, "testbrand", "instagram", "profile");

    private static ResolvedProviderCredential MakeCredential(string? mappingsJson = null, string apiKey = "test-key") =>
        new(
            Guid.NewGuid(),
            "apify",
            IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string>
            {
                ["api_key"] = apiKey,
                ["actor_mappings_json"] = mappingsJson ?? ActorMappingsJson
            },
            SelectedModel: null);

    // ── Default actor succeeds — no fallback needed ──────────────────────────

    [Fact]
    public async Task ResolveAsync_DefaultActorSuccess_ReturnsMappedResult()
    {
        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(GoldenFixture);

        var resolver = new ApifyProfileScrapeResolver(
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.ResolveAsync(
            MakeRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("testbrand", result.Value.Profile.Username);
        Assert.Empty(result.Value.Diagnostics);
        // The golden fixture contains one _degraded post (post-004), so IsPartial is true (PA-08).
        // Fallback was NOT used — no diagnostics. IsPartial here signals degraded posts only.
        Assert.True(result.Value.IsPartial);
    }

    // ── PA-06: default actor succeeds but returns no profile item → fallback to backup ──

    [Fact]
    public async Task ResolveAsync_DefaultActorNoProfileItem_FallsBackToBackup()
    {
        var noProfileResponse = JsonSerializer.Serialize(new[]
        {
            new { resultType = "quality_report" },
            new { resultType = "bandwidth_report" }
        });

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(noProfileResponse);  // default: no profile item
        handler.EnqueueSuccess(GoldenFixture);       // backup: golden fixture with profile

        var resolver = new ApifyProfileScrapeResolver(
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.ResolveAsync(
            MakeRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Diagnostics); // one failure diagnostic for the default actor
        Assert.True(result.Value.IsPartial);     // fallback used → partial
    }

    // ── PA-05: default actor HTTP error → backup actor invoked ────────────────

    [Fact]
    public async Task ResolveAsync_DefaultActorHttpError_FallsBackToBackupActor()
    {
        var handler = new ProgrammableHandler();
        handler.EnqueueError(HttpStatusCode.InternalServerError); // default fails
        handler.EnqueueSuccess(GoldenFixture);                    // backup succeeds

        var resolver = new ApifyProfileScrapeResolver(
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.ResolveAsync(
            MakeRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Diagnostics);
        Assert.True(result.Value.IsPartial);
        Assert.Equal(2, handler.CallCount);
    }

    // ── PA-05: both actors fail → failure result with two diagnostics ─────────

    [Fact]
    public async Task ResolveAsync_BothActorsFail_ReturnsFailureWithTwoDiagnostics()
    {
        var handler = new ProgrammableHandler();
        handler.EnqueueError(HttpStatusCode.InternalServerError);
        handler.EnqueueError(HttpStatusCode.ServiceUnavailable);

        var resolver = new ApifyProfileScrapeResolver(
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.ResolveAsync(
            MakeRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(2, handler.CallCount);
    }

    // ── PA-23: diagnostics populated on fallback run ───────────────────────────

    [Fact]
    public async Task ResolveAsync_FallbackRun_DiagnosticContainsFailureInfo()
    {
        var handler = new ProgrammableHandler();
        handler.EnqueueError(HttpStatusCode.InternalServerError); // default fails
        handler.EnqueueSuccess(GoldenFixture);                    // backup succeeds

        var resolver = new ApifyProfileScrapeResolver(
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.ResolveAsync(
            MakeRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var diagnostic = result.Value.Diagnostics.Single();
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.FailureCode));
        Assert.Equal(1, diagnostic.AttemptNumber);
        Assert.True(result.Value.IsPartial);
    }

    // ── PA-04: actor-specific request body ───────────────────────────────────

    [Fact]
    public async Task ResolveAsync_GetLeadsActorInput_ContainsResidentialProxyTier()
    {
        var capturingHandler = new CapturingBodyHandler(GoldenFixture);

        var resolver = new ApifyProfileScrapeResolver(
            MakeCredential(),
            new StaticHttpClientFactory(capturingHandler));

        await resolver.ResolveAsync(
            MakeRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.NotNull(capturingHandler.CapturedBody);
        Assert.Contains("\"profiles\"", capturingHandler.CapturedBody);
        Assert.Contains("\"proxyTier\":\"residential\"", capturingHandler.CapturedBody);
        Assert.Contains("\"instagram-profile-scraper\"", capturingHandler.CapturedBody);
        Assert.DoesNotContain("\"proxy\"", capturingHandler.CapturedBody);
    }

    [Fact]
    public async Task ResolveAsync_OfficialApifyProfileActor_UsesUsernamesInput()
    {
        var officialMappingsJson = JsonSerializer.Serialize(new[]
        {
            new { networkKey = "instagram", variantKey = "profile", actorId = "apify/instagram-profile-scraper", isPrimary = true }
        });
        var officialFixture = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = "242598499",
                username = "testbrand",
                followersCount = 150000,
                followsCount = 200,
                postsCount = 100,
                verified = true,
                latestPosts = new[]
                {
                    new
                    {
                        shortCode = "ABC123",
                        mediaType = "Image",
                        likesCount = 1200,
                        commentsCount = 30,
                        timestamp = "2026-05-01T10:00:00.000Z",
                        ownerUsername = "testbrand"
                    }
                }
            }
        });
        var capturingHandler = new CapturingBodyHandler(officialFixture);

        var resolver = new ApifyProfileScrapeResolver(
            MakeCredential(mappingsJson: officialMappingsJson),
            new StaticHttpClientFactory(capturingHandler));

        var result = await resolver.ResolveAsync(
            MakeRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {(result.IsFailure ? result.Error.Code : "n/a")}");
        Assert.NotNull(capturingHandler.CapturedBody);
        Assert.Contains("\"usernames\"", capturingHandler.CapturedBody);
        Assert.Contains("\"testbrand\"", capturingHandler.CapturedBody);
        Assert.DoesNotContain("\"profiles\"", capturingHandler.CapturedBody);
    }

    // ── PA-24: default actor used first; actorId corresponds to profile mapping ──

    [Fact]
    public async Task ResolveAsync_UsesDefaultActorIdFirst()
    {
        var capturingHandler = new CapturingBodyHandler(GoldenFixture);

        var resolver = new ApifyProfileScrapeResolver(
            MakeCredential(),
            new StaticHttpClientFactory(capturingHandler));

        await resolver.ResolveAsync(
            MakeRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        // URL should contain the default actor id (URL-encoded '~' → '%7E')
        Assert.Contains(
            Uri.EscapeDataString("get-leads~all-in-one-instagram-scraper"),
            capturingHandler.CapturedUri?.ToString() ?? string.Empty);
    }

    // ── INT-01: missing actor_mappings_json for profile variant → 422 ─────────

    [Fact]
    public async Task ResolveAsync_MissingProfileActorMapping_ReturnsProfileActorNotConfigured()
    {
        var singleVideoMappingsOnly = JsonSerializer.Serialize(new[]
        {
            new { networkKey = "instagram", variantKey = "single-video", actorId = "actor/single-video" }
        });

        var handler = new ProgrammableHandler();
        var resolver = new ApifyProfileScrapeResolver(
            MakeCredential(mappingsJson: singleVideoMappingsOnly),
            new StaticHttpClientFactory(handler));

        var result = await resolver.ResolveAsync(
            MakeRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, handler.CallCount); // no HTTP call made
        Assert.Contains("profile_actor_not_configured", result.Error.Code,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── INT-02: missing Apify integration (no api_key) → ApifyNotConfigured ──

    [Fact]
    public async Task ResolveAsync_MissingApiKey_ReturnsApifyNotConfigured()
    {
        var credentialWithNoKey = new ResolvedProviderCredential(
            Guid.NewGuid(),
            "apify",
            IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string>
            {
                ["actor_mappings_json"] = ActorMappingsJson
                // no api_key
            },
            SelectedModel: null);

        var handler = new ProgrammableHandler();
        var resolver = new ApifyProfileScrapeResolver(
            credentialWithNoKey,
            new StaticHttpClientFactory(handler));

        var result = await resolver.ResolveAsync(
            MakeRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, handler.CallCount);
        Assert.Contains("apify_not_configured", result.Error.Code,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── Username parsed from Instagram URL ────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ProfileUrlInput_ParsesUsernameFromUrl()
    {
        var capturingHandler = new CapturingBodyHandler(GoldenFixture);

        var resolver = new ApifyProfileScrapeResolver(
            MakeCredential(),
            new StaticHttpClientFactory(capturingHandler));

        var request = new ProfileScrapeRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ClientId: null,
            ProfileUrlOrHandle: "https://www.instagram.com/testbrand/",
            PlatformKey: "instagram",
            CapabilityKey: "profile");

        await resolver.ResolveAsync(request, CancellationToken.None);

        Assert.NotNull(capturingHandler.CapturedBody);
        Assert.Contains("testbrand", capturingHandler.CapturedBody);
    }

    // ── Own-account handle (no URL) ───────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_OwnAccountHandle_UsesHandleDirectly()
    {
        var capturingHandler = new CapturingBodyHandler(GoldenFixture);

        var resolver = new ApifyProfileScrapeResolver(
            MakeCredential(),
            new StaticHttpClientFactory(capturingHandler));

        var request = new ProfileScrapeRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ClientId: Guid.NewGuid(),
            ProfileUrlOrHandle: "testbrand",
            PlatformKey: "instagram",
            CapabilityKey: "profile");

        await resolver.ResolveAsync(request, CancellationToken.None);

        Assert.NotNull(capturingHandler.CapturedBody);
        Assert.Contains("testbrand", capturingHandler.CapturedBody);
    }

    // ── Single-video path is NOT invoked during profile fallback ─────────────

    [Fact]
    public async Task ResolveAsync_Fallback_DoesNotInvokeWorldOneActorRouting()
    {
        // The single-video actor (variantKey "single-video") must NOT appear in the request URI
        // when we resolve a profile scrape (variantKey "profile").
        var singleVideoActor = "single-video-actor-id";
        var mappings = JsonSerializer.Serialize(new[]
        {
            new { networkKey = "instagram", variantKey = "single-video", actorId = singleVideoActor },
            new { networkKey = "instagram", variantKey = "profile", actorId = "get-leads~all-in-one-instagram-scraper" },
            new { networkKey = "instagram", variantKey = "profile", actorId = "apify~instagram-profile-scraper" }
        });

        var capturingHandler = new CapturingBodyHandler(GoldenFixture);

        var resolver = new ApifyProfileScrapeResolver(
            MakeCredential(mappingsJson: mappings),
            new StaticHttpClientFactory(capturingHandler));

        await resolver.ResolveAsync(
            MakeRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.DoesNotContain(
            Uri.EscapeDataString(singleVideoActor),
            capturingHandler.CapturedUri?.ToString() ?? string.Empty);
    }

    // ── GetAllMappedActors — primary-first ordering ──────────────────────────────

    /// <summary>
    /// When two "profile" entries exist and the second has isPrimary=true,
    /// GetAllMappedActors returns the primary entry FIRST, then the backup.
    /// </summary>
    [Fact]
    public void GetAllMappedActors_SecondEntryIsPrimary_ReturnsPrimaryFirst()
    {
        // Arrange — second entry marked as primary (user designated it after registration)
        var json = JsonSerializer.Serialize(new[]
        {
            new { networkKey = "instagram", variantKey = "profile", actorId = "actor-a", isPrimary = false },
            new { networkKey = "instagram", variantKey = "profile", actorId = "actor-b", isPrimary = true }
        });

        // Act
        var result = ApifyProfileScrapeResolver.GetAllMappedActors(json, "instagram", "profile");

        // Assert — primary first, backup second
        Assert.Equal(2, result.Count);
        Assert.Equal("actor-b", result[0]); // primary
        Assert.Equal("actor-a", result[1]); // backup
    }

    /// <summary>
    /// When the first entry has isPrimary=true, array order is preserved (primary is already first).
    /// </summary>
    [Fact]
    public void GetAllMappedActors_FirstEntryIsPrimary_ReturnsInArrayOrder()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new { networkKey = "instagram", variantKey = "profile", actorId = "actor-primary", isPrimary = true },
            new { networkKey = "instagram", variantKey = "profile", actorId = "actor-backup", isPrimary = false }
        });

        var result = ApifyProfileScrapeResolver.GetAllMappedActors(json, "instagram", "profile");

        Assert.Equal(2, result.Count);
        Assert.Equal("actor-primary", result[0]);
        Assert.Equal("actor-backup", result[1]);
    }

    /// <summary>
    /// Entries missing isPrimary (old JSON, pre-backfill) are tolerated — all treated as non-primary,
    /// returned in JSON array order.
    /// </summary>
    [Fact]
    public void GetAllMappedActors_MissingIsPrimary_ToleratedReturnedInArrayOrder()
    {
        // Arrange — old JSON format without isPrimary field
        var json = "[{\"networkKey\":\"instagram\",\"variantKey\":\"profile\",\"actorId\":\"actor-1\"},{\"networkKey\":\"instagram\",\"variantKey\":\"profile\",\"actorId\":\"actor-2\"}]";

        var result = ApifyProfileScrapeResolver.GetAllMappedActors(json, "instagram", "profile");

        // Both returned; no crash; order preserved
        Assert.Equal(2, result.Count);
        Assert.Equal("actor-1", result[0]);
        Assert.Equal("actor-2", result[1]);
    }

    /// <summary>
    /// "default" variant entries are NOT used as fallback anymore — profile resolver
    /// requires explicit "profile" variant entries only. Empty result → ProfileActorNotConfigured.
    /// </summary>
    [Fact]
    public void GetAllMappedActors_OnlyDefaultVariant_ReturnsEmpty_NoFallback()
    {
        // Arrange — only "default" entries (no "profile" entries)
        var json = JsonSerializer.Serialize(new[]
        {
            new { networkKey = "instagram", variantKey = "default", actorId = "actor-default-1", isPrimary = false },
            new { networkKey = "instagram", variantKey = "default", actorId = "actor-default-2", isPrimary = false }
        });

        // Act
        var result = ApifyProfileScrapeResolver.GetAllMappedActors(json, "instagram", "profile");

        // Assert — no fallback to "default"; empty list → caller will return ProfileActorNotConfigured
        Assert.Empty(result);
    }

    /// <summary>
    /// No Instagram entries at all → empty list → upstream caller returns ProfileActorNotConfigured.
    /// </summary>
    [Fact]
    public void GetAllMappedActors_NoInstagramEntries_ReturnsEmpty()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new { networkKey = "tiktok", variantKey = "profile", actorId = "tiktok-actor", isPrimary = true }
        });

        var result = ApifyProfileScrapeResolver.GetAllMappedActors(json, "instagram", "profile");

        Assert.Empty(result);
    }

    /// <summary>
    /// End-to-end: "default" entries alone (no "profile" entries) → ProfileActorNotConfigured.
    /// The seed/backfill is responsible for re-tagging "default" to "profile"; after seed this
    /// test no longer applies to production data, but it validates the guard works pre-seed.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_OnlyDefaultVariant_ReturnsProfileActorNotConfigured()
    {
        var defaultOnlyMappingsJson = JsonSerializer.Serialize(new[]
        {
            new { networkKey = "instagram", variantKey = "default", actorId = "actor-default", isPrimary = false }
        });

        var handler = new ProgrammableHandler();
        var resolver = new ApifyProfileScrapeResolver(
            MakeCredential(mappingsJson: defaultOnlyMappingsJson),
            new StaticHttpClientFactory(handler));

        var result = await resolver.ResolveAsync(
            MakeRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, handler.CallCount); // no HTTP call made
        Assert.Contains("profile_actor_not_configured", result.Error.Code,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// After the backfill seed runs, UI-style "default" entries become "profile" entries.
    /// When actor_mappings_json contains seeded "profile" entries (variantKey="profile"),
    /// ResolveAsync successfully dispatches and returns the profile result.
    /// This is the regression guard for correctly seeded production data.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_SeededProfileVariantMappings_ResolvesProfileSuccessfully()
    {
        // Arrange — JSON as written by the backfill seeder (variantKey: "profile", isPrimary on first)
        var seededMappingsJson = JsonSerializer.Serialize(new[]
        {
            new { networkKey = "instagram", variantKey = "profile", actorId = "get-leads~all-in-one-instagram-scraper", isPrimary = true },
            new { networkKey = "instagram", variantKey = "profile", actorId = "apify~instagram-profile-scraper", isPrimary = false }
        });

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(GoldenFixture);

        var resolver = new ApifyProfileScrapeResolver(
            MakeCredential(mappingsJson: seededMappingsJson),
            new StaticHttpClientFactory(handler));

        // Act
        var result = await resolver.ResolveAsync(
            MakeRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        // Assert — profile scrape must succeed
        Assert.True(result.IsSuccess, $"Expected success but got: {(result.IsFailure ? result.Error.Code : "n/a")}");
        Assert.Equal(1, handler.CallCount); // primary actor called exactly once
        Assert.Equal("testbrand", result.Value.Profile.Username);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class ProgrammableHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();
        public int CallCount { get; private set; }

        public void EnqueueSuccess(string body)
        {
            _responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        public void EnqueueError(HttpStatusCode code)
        {
            _responses.Enqueue(() => new HttpResponseMessage(code)
            {
                Content = new StringContent("{\"error\":\"simulated failure\"}")
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (_responses.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(_responses.Dequeue()());
        }
    }

    private sealed class CapturingBodyHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        public string? CapturedBody { get; private set; }
        public Uri? CapturedUri { get; private set; }

        public CapturingBodyHandler(string responseBody) => _responseBody = responseBody;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CapturedUri = request.RequestUri;
            if (request.Content is not null)
            {
                CapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StaticHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
