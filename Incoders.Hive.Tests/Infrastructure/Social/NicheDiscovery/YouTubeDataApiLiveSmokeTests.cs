using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.NicheDiscovery;
using Incoders.Hive.Infrastructure.Social.NicheDiscovery;
using Incoders.Hive.Infrastructure.Social.NicheDiscovery.Origins;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Incoders.Hive.Tests.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// LIVE smoke test against the real YouTube Data API v3. Every other test in this suite fakes the
/// transport, which proves our wiring but never proves the PROVIDER contract still holds — the shape of
/// a chart response, whether the key authenticates, whether a channel still carries an avatar. That gap
/// is exactly where a "green suite, broken feature" lives.
///
/// OPT-IN BY DESIGN. Without <c>HIVE_YOUTUBE_DATA_API_KEY</c> set, these tests return immediately: a
/// suite that needs a secret to pass is a suite that fails for everyone who does not have one, and CI
/// would go permanently red. To run it:
///
/// <code>
/// $env:HIVE_YOUTUBE_DATA_API_KEY = "&lt;key&gt;"
/// dotnet test Incoders.Hive.Tests --filter "FullyQualifiedName~YouTubeDataApiLiveSmoke"
/// </code>
///
/// DELIBERATELY TINY: one region, a handful of chart calls, one channel batch — a few quota units out of
/// the 10,000/day default. It answers "does the integration work at all", not "is the data good"; one
/// harvested channel is a pass.
/// </summary>
public sealed class YouTubeDataApiLiveSmokeTests
{
    private const string ApiKeyVariable = "HIVE_YOUTUBE_DATA_API_KEY";
    private const string BaseAddress = "https://www.googleapis.com/youtube/v3";

    /// <summary>One region and a small ceiling keep the whole file in single-digit quota units.</summary>
    private static readonly string[] SamplingRegions = ["AR"];
    private static readonly DiscoveryBudget SmallBudget = new(6, 0);

    private readonly ITestOutputHelper _output;

    public YouTubeDataApiLiveSmokeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task TrendingSweep_AgainstTheRealApi_HarvestsAtLeastOneVideo()
    {
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _output.WriteLine($"SKIPPED: set {ApiKeyVariable} to run the live smoke test.");
            return;
        }

        var source = BuildSource(apiKey);

        var result = await source.DiscoverAsync(
            Query(DiscoveryStage.TrendingSweep), SmallBudget, CancellationToken.None);

        Assert.Equal(NicheSourceOutcome.Ran, result.Outcome);
        Assert.NotEmpty(result.Videos);

        var video = result.Videos[0];
        _output.WriteLine($"Harvested {result.Videos.Count} video(s). First: {video.ExternalRef} — {video.Title}");

        // The fields the whole pipeline is built on. A provider change that empties any of these turns
        // every downstream metric into NotReported without anything failing loudly.
        Assert.False(string.IsNullOrWhiteSpace(video.ExternalRef));
        Assert.False(string.IsNullOrWhiteSpace(video.ChannelExternalRef));
        Assert.NotNull(video.PublishedAtUtc);
    }

    /// <summary>
    /// The deliverable is a CHANNEL, so the smoke test follows the harvested video back to its channel and
    /// checks the fields the operator actually reads.
    /// </summary>
    [Fact]
    public async Task ChannelDetail_AgainstTheRealApi_ReturnsAReviewableChannel()
    {
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _output.WriteLine($"SKIPPED: set {ApiKeyVariable} to run the live smoke test.");
            return;
        }

        var source = BuildSource(apiKey);

        var sweep = await source.DiscoverAsync(
            Query(DiscoveryStage.TrendingSweep), SmallBudget, CancellationToken.None);
        Assert.NotEmpty(sweep.Videos);

        var channelRef = sweep.Videos[0].ChannelExternalRef;

        var detail = await source.DiscoverAsync(
            Query(DiscoveryStage.ChannelDetail) with { ChannelExternalRefs = [channelRef] },
            SmallBudget,
            CancellationToken.None);

        Assert.Equal(NicheSourceOutcome.Ran, detail.Outcome);
        var channel = Assert.Single(detail.Channels);

        _output.WriteLine(
            $"Channel {channel.ExternalRef} — {channel.Handle} | subs={channel.SubscriberCount} "
                + $"| views={channel.TotalViews} | country={channel.Country} "
                + $"| avatar={(channel.ThumbnailUrl is null ? "none" : "yes")}");

        Assert.Equal(channelRef, channel.ExternalRef);
        Assert.False(string.IsNullOrWhiteSpace(channel.Handle));

        // Subscriber count may legitimately be hidden by the owner, so it is not asserted. The avatar is
        // what the operator scans the list by, and the provider returns it for every channel.
        Assert.False(string.IsNullOrWhiteSpace(channel.ThumbnailUrl));
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static NicheDiscoveryQuery Query(DiscoveryStage stage) => new(
        Guid.NewGuid(), stage, "youtube", "video", "es", DiscoveryWindow.Default(), [], [], []);

    private static YouTubeDataApiNicheDiscoverySource BuildSource(string apiKey)
    {
        var options = Options.Create(new NicheDiscoveryOriginOptions
        {
            BaseAddress = BaseAddress,
            SamplingRegions = SamplingRegions,
        });

        var configuration = new NicheDiscoveryOriginConfiguration(
            new EnabledStepResolver(), new EnvironmentApiKeyResolver(apiKey), options);

        return new YouTubeDataApiNicheDiscoverySource(
            configuration, new YouTubeDataApiClient(new RealHttpClientFactory()));
    }

    private sealed class RealHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { Timeout = TimeSpan.FromSeconds(30) };
    }

    private sealed class EnabledStepResolver : IWorkflowConfigurationResolver
    {
        public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
            WorkflowStepResolutionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
                [new WorkflowStepRouteCandidate(
                    ProviderId: Guid.NewGuid(),
                    ProviderKey: "youtube-data-api-v3",
                    OrganizationIntegrationId: Guid.NewGuid(),
                    ConnectionMode: IntegrationConnectionMode.BringYourOwnKey,
                    Priority: 1,
                    ModelId: null,
                    ActorId: null,
                    IsEnabled: true)]));
    }

    private sealed class EnvironmentApiKeyResolver : IIntegrationResolver
    {
        private readonly string _apiKey;

        public EnvironmentApiKeyResolver(string apiKey) => _apiKey = apiKey;

        public Task<Result<ResolvedIntegration>> ResolveProviderAsync(
            Guid organizationId, string providerKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(new ResolvedIntegration(
                organizationId,
                providerKey,
                "YouTube Data API v3 (live smoke)",
                IntegrationProviderCategory.DataExtraction,
                IntegrationConnectionMode.BringYourOwnKey,
                IntegrationStatus.Connected,
                IsReadOnly: false,
                SelectedAiModel: null,
                Credentials: new Dictionary<string, string> { ["api_key"] = _apiKey })));
    }
}
