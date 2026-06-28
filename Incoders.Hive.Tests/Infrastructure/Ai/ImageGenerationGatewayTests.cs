using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
// WU-9: alias removed — IImageGenerationClient retired; ImageGenerationRequest is unambiguous now.

namespace Incoders.Hive.Tests.Infrastructure.Ai;

/// <summary>
/// WU-5 RED tests: ImageGenerationGateway resolves the "generate-image" step
/// via IWorkflowConfigurationResolver, creates IImageModelClient via IExternalProviderClientFactory,
/// and delegates GenerateAsync. Mirrors AiCompletionGatewayTests pattern.
/// Refs: IMG-01, IMG-02, IMG-06; design §2.3; tasks 5.1.
/// </summary>
public sealed partial class ImageGenerationGatewayTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    // -----------------------------------------------------------------------
    // 5.1a: Given a route returned by the resolver, GenerateAsync delegates to the model client
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_ValidRoute_DelegatesToModelClientAndReturnsResult()
    {
        var expectedBytes = new byte[] { 1, 2, 3 };
        var candidate = BuildCandidate("openai", "dall-e-3");

        var resolver = new FakeWorkflowResolver(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([candidate]));
        var credentialResolver = new FakeCredentialResolver(Result.Success(BuildCredential("openai", "dall-e-3")));
        var modelClient = new CapturingImageModelClient(Result.Success(
            new ImageGenerationResult(expectedBytes, "image/png", "openai", "dall-e-3")));
        var factory = new FakeImageClientFactory(modelClient);

        var gateway = new ImageGenerationGateway(resolver, credentialResolver, factory, NullLogger<ImageGenerationGateway>.Instance);

        var result = await gateway.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedBytes, result.Value.Bytes);
        Assert.Equal("openai", result.Value.ProviderKey);
        Assert.Equal(1, modelClient.CallCount);
    }

    // -----------------------------------------------------------------------
    // 5.1b: Resolver returns no route → Result.Failure without calling any client (IMG-06-a)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_NoRouteConfigured_ReturnsFailureWithoutCallingClient()
    {
        var resolver = new FakeWorkflowResolver(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));
        var credentialResolver = new FakeCredentialResolver(Result.Failure<ResolvedProviderCredential>(
            IntegrationErrors.AiRouteNotConfigured));
        var modelClient = new CapturingImageModelClient(Result.Success(
            new ImageGenerationResult([1], "image/png", "openai", "dall-e-3")));
        var factory = new FakeImageClientFactory(modelClient);

        var gateway = new ImageGenerationGateway(resolver, credentialResolver, factory, NullLogger<ImageGenerationGateway>.Instance);

        var result = await gateway.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, modelClient.CallCount);
    }

    // -----------------------------------------------------------------------
    // 5.1c: OrganizationId from the request is forwarded to the resolver
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_ForwardsOrganizationIdToResolver()
    {
        var capturedRequests = new List<WorkflowStepResolutionRequest>();
        var resolver = new CapturingWorkflowResolver(capturedRequests);
        var credentialResolver = new FakeCredentialResolver(Result.Failure<ResolvedProviderCredential>(
            IntegrationErrors.AiRouteNotConfigured));
        var factory = new FakeImageClientFactory(null!);

        var gateway = new ImageGenerationGateway(resolver, credentialResolver, factory, NullLogger<ImageGenerationGateway>.Instance);

        await gateway.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.Single(capturedRequests);
        Assert.Equal(OrgId, capturedRequests[0].OrganizationId);
        Assert.Equal("generate-image", capturedRequests[0].StepKey);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ImageGenerationRequest BuildRequest() => new(
        OrganizationId: OrgId,
        CapabilityKey: "ai-image",
        PromptVersionKey: "image-gen/v1",
        Prompt: "a botanical background",
        Width: 1024,
        Height: 1024,
        PreferredProviderKey: null,
        PreferredModel: null);

    private static WorkflowStepRouteCandidate BuildCandidate(string providerKey, string modelId) =>
        new(Guid.NewGuid(), providerKey, Guid.NewGuid(), IntegrationConnectionMode.BringYourOwnKey, 1, modelId, null, true);

    private static ResolvedProviderCredential BuildCredential(string providerKey, string model) =>
        new(Guid.Empty, providerKey, IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string> { ["api_key"] = "test-key" },
            SelectedModel: model);
}

// -----------------------------------------------------------------------
// Test doubles
// -----------------------------------------------------------------------

file sealed class FakeWorkflowResolver : IWorkflowConfigurationResolver
{
    private readonly Result<IReadOnlyList<WorkflowStepRouteCandidate>> _result;
    public FakeWorkflowResolver(Result<IReadOnlyList<WorkflowStepRouteCandidate>> result) => _result = result;

    public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
        WorkflowStepResolutionRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_result);
}

file sealed class CapturingWorkflowResolver : IWorkflowConfigurationResolver
{
    private readonly List<WorkflowStepResolutionRequest> _captured;

    public CapturingWorkflowResolver(List<WorkflowStepResolutionRequest> captured)
        => _captured = captured;

    public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
        WorkflowStepResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        _captured.Add(request);
        return Task.FromResult(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));
    }
}

file sealed class FakeCredentialResolver : IIntegrationCredentialResolver
{
    private readonly Result<ResolvedProviderCredential> _result;
    public FakeCredentialResolver(Result<ResolvedProviderCredential> result) => _result = result;

    public Task<Result<ResolvedProviderCredential>> ResolveAsync(
        Guid organizationId,
        IntegrationRouteCandidate candidate,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_result);
}

file sealed class CapturingImageModelClient : IImageModelClient
{
    private readonly Result<ImageGenerationResult> _result;
    private int _callCount;
    public int CallCount => _callCount;

    public ImageModelCapabilities Capabilities { get; } = new(false, 0);

    public CapturingImageModelClient(Result<ImageGenerationResult> result) => _result = result;

    public Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken ct = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(_result);
    }
}

file sealed class FakeImageClientFactory : IExternalProviderClientFactory
{
    private readonly IImageModelClient _client;
    public FakeImageClientFactory(IImageModelClient client) => _client = client;

    public TClient Create<TClient>(ResolvedProviderCredential credential)
        where TClient : notnull
    {
        return (TClient)(object)_client;
    }
}
