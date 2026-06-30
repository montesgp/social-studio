using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration;

/// <summary>
/// WU-3 RED tests: ImagePromptWriter implements IImagePromptWriter over the TEXT route
/// via a fake IAiCompletionGateway.
/// Also covers route-resolution: WriteAsync resolves the org's AI route via
/// IWorkflowConfigurationResolver before calling the gateway (bug fix: was always null).
/// Refs: IMG-03, CIG-CG-04; design §3.2, OQ-6; tasks 3.1.
/// </summary>
public sealed class ImagePromptWriterTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();

    private static ClientAestheticProfile RichProfile() => new(
        BackgroundDescriptors: ["dark botanical", "hand-drawn textures"],
        VoiceTone: "warm and approachable",
        AverageSlidesPerCarousel: 5.0,
        AnalyzedAtUtc: DateTime.UtcNow,
        StyleDescriptors: ["organic", "earthy", "natural light"],
        Motifs: ["botanical leaves", "wooden textures"],
        CaptionVoice: "educational yet friendly",
        Palette: [new AestheticPaletteEntry("#3D2B1F", "primary", 0), new AestheticPaletteEntry("#A8C5A0", "accent", 1)]);

    // -----------------------------------------------------------------------
    // 3.1a: Rich aesthetic profile → gateway receives structured JSON request with
    //        CapabilityKey = "ai-generation" and PromptVersionKey = "image-prompt/v1"
    // -----------------------------------------------------------------------

    [Fact]
    public async Task WriteAsync_RichProfile_CallsGatewayWithCorrectCapabilityAndVersion()
    {
        var capturedRequest = (AiJsonCompletionRequest?)null;
        var fakeGateway = new FakeAiCompletionGateway(
            onRequest: req => capturedRequest = req,
            response: """{"imagePrompt": "warm hand-drawn botanical scene"}""");

        var writer = new ImagePromptWriter(fakeGateway, FakeWorkflowConfigurationResolver.Default(), NullLogger<ImagePromptWriter>.Instance);

        var result = await writer.WriteAsync(new ImagePromptRequest(
            OrgId, ClientId, SlideIndex: 1,
            SlideCopySummary: "Discover the power of natural ingredients",
            Aesthetic: RichProfile()));

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedRequest);
        Assert.Equal("ai-generation", capturedRequest!.CapabilityKey);
        Assert.Equal("image-prompt/v1", capturedRequest.PromptVersionKey);
    }

    // -----------------------------------------------------------------------
    // 3.1b: SlideCopySummary appears in the assembled prompt (on-topic prompting)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task WriteAsync_SlideCopySummary_AppearsInPromptSentToGateway()
    {
        var capturedRequest = (AiJsonCompletionRequest?)null;
        var fakeGateway = new FakeAiCompletionGateway(
            onRequest: req => capturedRequest = req,
            response: """{"imagePrompt": "scene matching the copy"}""");

        var writer = new ImagePromptWriter(fakeGateway, FakeWorkflowConfigurationResolver.Default(), NullLogger<ImagePromptWriter>.Instance);

        const string CopySummary = "Unlock your creative potential today";

        await writer.WriteAsync(new ImagePromptRequest(
            OrgId, ClientId, SlideIndex: 2,
            SlideCopySummary: CopySummary,
            Aesthetic: RichProfile()));

        Assert.NotNull(capturedRequest);
        Assert.Contains(CopySummary, capturedRequest!.InputJson, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // 3.1c: Response { "imagePrompt": "..." } is parsed and returned as Result.Success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task WriteAsync_ValidResponse_ReturnsSuccessWithPromptText()
    {
        const string ExpectedPrompt = "warm hand-drawn botanical scene, earthy tones, golden hour light";
        var fakeGateway = new FakeAiCompletionGateway(
            onRequest: _ => { },
            response: $$"""{ "imagePrompt": "{{ExpectedPrompt}}" }""");

        var writer = new ImagePromptWriter(fakeGateway, FakeWorkflowConfigurationResolver.Default(), NullLogger<ImagePromptWriter>.Instance);

        var result = await writer.WriteAsync(new ImagePromptRequest(
            OrgId, ClientId, SlideIndex: 1,
            SlideCopySummary: "Nature is healing",
            Aesthetic: RichProfile()));

        Assert.True(result.IsSuccess);
        Assert.Equal(ExpectedPrompt, result.Value);
    }

    // -----------------------------------------------------------------------
    // 3.1d: Empty imagePrompt in the response → Result.Failure (IMG-03-b degrade trigger)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task WriteAsync_EmptyImagePromptInResponse_ReturnsFailure()
    {
        var fakeGateway = new FakeAiCompletionGateway(
            onRequest: _ => { },
            response: """{"imagePrompt": ""}""");

        var writer = new ImagePromptWriter(fakeGateway, FakeWorkflowConfigurationResolver.Default(), NullLogger<ImagePromptWriter>.Instance);

        var result = await writer.WriteAsync(new ImagePromptRequest(
            OrgId, ClientId, SlideIndex: 1,
            SlideCopySummary: "Test",
            Aesthetic: RichProfile()));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task WriteAsync_NullImagePromptInResponse_ReturnsFailure()
    {
        var fakeGateway = new FakeAiCompletionGateway(
            onRequest: _ => { },
            response: """{"imagePrompt": null}""");

        var writer = new ImagePromptWriter(fakeGateway, FakeWorkflowConfigurationResolver.Default(), NullLogger<ImagePromptWriter>.Instance);

        var result = await writer.WriteAsync(new ImagePromptRequest(
            OrgId, ClientId, SlideIndex: 1,
            SlideCopySummary: "Test",
            Aesthetic: RichProfile()));

        Assert.True(result.IsFailure);
    }

    // -----------------------------------------------------------------------
    // 3.1e: Gateway failure → Result.Failure (degrade trigger)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task WriteAsync_GatewayFailure_ReturnsFailure()
    {
        var fakeGateway = new FailingAiCompletionGateway(
            Error.Failure("provider-unavailable", "provider.unavailable"));

        var writer = new ImagePromptWriter(fakeGateway, FakeWorkflowConfigurationResolver.Default(), NullLogger<ImagePromptWriter>.Instance);

        var result = await writer.WriteAsync(new ImagePromptRequest(
            OrgId, ClientId, SlideIndex: 1,
            SlideCopySummary: "Test",
            Aesthetic: RichProfile()));

        Assert.True(result.IsFailure);
    }

    // -----------------------------------------------------------------------
    // 3.1f: OrganizationId is forwarded to the gateway
    // -----------------------------------------------------------------------

    [Fact]
    public async Task WriteAsync_ForwardsOrganizationIdToGateway()
    {
        var capturedRequest = (AiJsonCompletionRequest?)null;
        var fakeGateway = new FakeAiCompletionGateway(
            onRequest: req => capturedRequest = req,
            response: """{"imagePrompt": "botanical warmth"}""");

        var writer = new ImagePromptWriter(fakeGateway, FakeWorkflowConfigurationResolver.Default(), NullLogger<ImagePromptWriter>.Instance);

        await writer.WriteAsync(new ImagePromptRequest(
            OrgId, ClientId, SlideIndex: 1,
            SlideCopySummary: "Fresh and organic",
            Aesthetic: RichProfile()));

        Assert.NotNull(capturedRequest);
        Assert.Equal(OrgId, capturedRequest!.OrganizationId);
    }

    // -----------------------------------------------------------------------
    // Route-resolution tests (bug fix: provider key was always null → CSS fallback)
    // -----------------------------------------------------------------------

    /// <summary>
    /// When the resolver returns a priority-1 enabled candidate with ProviderKey = "claude-p",
    /// WriteAsync must forward that key (and ModelId) to the gateway.
    /// This is the core regression guard for the "always null → CSS" bug.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ResolvedProvider_ForwardsProviderKeyAndModelToGateway()
    {
        var capturedRequest = (AiJsonCompletionRequest?)null;
        var fakeGateway = new FakeAiCompletionGateway(
            onRequest: req => capturedRequest = req,
            response: """{"imagePrompt": "botanical warmth"}""");
        var resolver = FakeWorkflowConfigurationResolver.WithCandidate("claude-p", "claude-opus-4-8");

        var writer = new ImagePromptWriter(fakeGateway, resolver, NullLogger<ImagePromptWriter>.Instance);

        var result = await writer.WriteAsync(new ImagePromptRequest(
            OrgId, ClientId, SlideIndex: 1,
            SlideCopySummary: "Test",
            Aesthetic: RichProfile()));

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedRequest);
        Assert.Equal("claude-p", capturedRequest!.PreferredProviderKey);
        Assert.Equal("claude-opus-4-8", capturedRequest.PreferredModel);
    }

    /// <summary>
    /// When the resolver returns a failure, WriteAsync must return a failure Result
    /// and must NOT call the gateway (slide degrades to CSS gracefully).
    /// </summary>
    [Fact]
    public async Task WriteAsync_ResolverFailure_ReturnsFailureWithoutCallingGateway()
    {
        var gatewayCallCount = 0;
        var fakeGateway = new FakeAiCompletionGateway(
            onRequest: _ => gatewayCallCount++,
            response: """{"imagePrompt": "never reached"}""");
        var resolver = FakeWorkflowConfigurationResolver.WithFailure(
            Error.Failure("route-not-found", "route.not.found"));

        var writer = new ImagePromptWriter(fakeGateway, resolver, NullLogger<ImagePromptWriter>.Instance);

        var result = await writer.WriteAsync(new ImagePromptRequest(
            OrgId, ClientId, SlideIndex: 1,
            SlideCopySummary: "Test",
            Aesthetic: RichProfile()));

        Assert.True(result.IsFailure);
        Assert.Equal(0, gatewayCallCount);
    }

    /// <summary>
    /// When the resolver returns success but no enabled candidate, WriteAsync must
    /// return a failure Result and must NOT call the gateway.
    /// </summary>
    [Fact]
    public async Task WriteAsync_NoEnabledRouteCandidate_ReturnsFailureWithoutCallingGateway()
    {
        var gatewayCallCount = 0;
        var fakeGateway = new FakeAiCompletionGateway(
            onRequest: _ => gatewayCallCount++,
            response: """{"imagePrompt": "never reached"}""");
        var resolver = FakeWorkflowConfigurationResolver.WithNoCandidates();

        var writer = new ImagePromptWriter(fakeGateway, resolver, NullLogger<ImagePromptWriter>.Instance);

        var result = await writer.WriteAsync(new ImagePromptRequest(
            OrgId, ClientId, SlideIndex: 1,
            SlideCopySummary: "Test",
            Aesthetic: RichProfile()));

        Assert.True(result.IsFailure);
        Assert.Equal(0, gatewayCallCount);
    }

    // -----------------------------------------------------------------------
    // Test fakes
    // -----------------------------------------------------------------------

    /// <summary>
    /// Fake gateway that captures the request and returns a canned JSON response payload.
    /// The TResponse is <see cref="ImagePromptResponse"/> (internal to the writer).
    /// We fake it by returning the raw JSON as the Output string — the gateway parses it.
    /// </summary>
    private sealed class FakeAiCompletionGateway : IAiCompletionGateway
    {
        private readonly Action<AiJsonCompletionRequest> _onRequest;
        private readonly string _responseJson;

        public FakeAiCompletionGateway(Action<AiJsonCompletionRequest> onRequest, string response)
        {
            _onRequest = onRequest;
            _responseJson = response;
        }

        public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
            AiJsonCompletionRequest request,
            CancellationToken cancellationToken = default)
            where TResponse : notnull
        {
            _onRequest(request);

            // Deserialize the canned JSON into TResponse
            var payload = System.Text.Json.JsonSerializer.Deserialize<TResponse>(
                _responseJson,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

            if (payload is null)
                return Task.FromResult(Result.Failure<AiJsonCompletionResponse<TResponse>>(
                    Error.Failure("deserialize-failed", "test.deserialize.failed")));

            var response = new AiJsonCompletionResponse<TResponse>(
                payload, "fake-provider", "fake-model", []);

            return Task.FromResult(Result.Success(response));
        }
    }

    private sealed class FailingAiCompletionGateway : IAiCompletionGateway
    {
        private readonly Error _error;

        public FailingAiCompletionGateway(Error error) => _error = error;

        public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
            AiJsonCompletionRequest request,
            CancellationToken cancellationToken = default)
            where TResponse : notnull =>
            Task.FromResult(Result.Failure<AiJsonCompletionResponse<TResponse>>(_error));
    }

    /// <summary>
    /// Fake resolver for use in ImagePromptWriter tests. Mirrors the style of
    /// <see cref="FakeAiCompletionGateway"/> — factory methods for common cases.
    /// </summary>
    private sealed class FakeWorkflowConfigurationResolver : IWorkflowConfigurationResolver
    {
        private readonly Result<IReadOnlyList<WorkflowStepRouteCandidate>> _result;

        private FakeWorkflowConfigurationResolver(Result<IReadOnlyList<WorkflowStepRouteCandidate>> result)
        {
            _result = result;
        }

        /// <summary>Returns a single priority-1 enabled claude-p candidate — use in tests that don't care about the route.</summary>
        public static FakeWorkflowConfigurationResolver Default() =>
            WithCandidate("claude-p", "claude-opus-4-8");

        public static FakeWorkflowConfigurationResolver WithCandidate(string providerKey, string? modelId = null)
        {
            IReadOnlyList<WorkflowStepRouteCandidate> candidates =
            [
                new WorkflowStepRouteCandidate(
                    ProviderId: Guid.NewGuid(),
                    ProviderKey: providerKey,
                    OrganizationIntegrationId: Guid.NewGuid(),
                    ConnectionMode: IntegrationConnectionMode.BringYourOwnKey,
                    Priority: 1,
                    ModelId: modelId,
                    ActorId: null,
                    IsEnabled: true)
            ];
            return new FakeWorkflowConfigurationResolver(Result.Success(candidates));
        }

        public static FakeWorkflowConfigurationResolver WithFailure(Error error) =>
            new(Result.Failure<IReadOnlyList<WorkflowStepRouteCandidate>>(error));

        public static FakeWorkflowConfigurationResolver WithNoCandidates() =>
            new(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
            WorkflowStepResolutionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }
}
