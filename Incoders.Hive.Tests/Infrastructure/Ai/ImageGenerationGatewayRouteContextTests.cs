using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Infrastructure.Ai;

public sealed class ImageGenerationGatewayRouteContextTests
{
    [Fact]
    public async Task GenerateAsync_UsesRequestRouteContext_WhenProvided()
    {
        var captured = new List<WorkflowStepResolutionRequest>();
        var gateway = new ImageGenerationGateway(
            new RouteCapturingWorkflowResolver(captured, Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([])),
            new RouteCredentialResolver(Result.Failure<ResolvedProviderCredential>(IntegrationErrors.AiRouteNotConfigured)),
            new RouteImageClientFactory(new RouteImageClient(Result.Success(new ImageGenerationResult([1], "image/png", "openai", "dall-e-3")))),
            new RoutePassthroughExecutionPolicy(),
            NullLogger<ImageGenerationGateway>.Instance);

        await gateway.GenerateAsync(BuildRequest() with
        {
            WorkflowKey = "content-generator",
            AnalysisType = "asset-generation",
            StepKey = "cover-image",
            OperationKey = "feed-carousel",
            NetworkKey = "instagram"
        });

        Assert.Single(captured);
        Assert.Equal("content-generator", captured[0].WorkflowKey);
        Assert.Equal("asset-generation", captured[0].AnalysisType);
        Assert.Equal("cover-image", captured[0].StepKey);
        Assert.Equal("feed-carousel", captured[0].OperationKey);
        Assert.Equal("instagram", captured[0].NetworkKey);
    }

    [Fact]
    public async Task GenerateAsync_PassesOrderedCandidatesThroughExecutionPolicy()
    {
        var lowPriority = BuildCandidate("openai", "dall-e-3", 2);
        var highPriority = BuildCandidate("claude-p", "claude-p-image-json", 1);
        var policy = new RouteCapturingExecutionPolicy();
        var gateway = new ImageGenerationGateway(
            new RouteCapturingWorkflowResolver([], Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([lowPriority, highPriority])),
            new RouteCredentialResolver(Result.Success(BuildCredential("claude-p", "claude-p-image-json"))),
            new RouteImageClientFactory(new RouteImageClient(Result.Success(new ImageGenerationResult([7], "image/png", "claude-p", "claude-p-image-json")))),
            policy,
            NullLogger<ImageGenerationGateway>.Instance);

        var result = await gateway.GenerateAsync(BuildRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(["claude-p", "openai"], policy.Candidates.Select(c => c.ProviderKey).ToArray());
    }

    private static ImageGenerationRequest BuildRequest() => new(
        Guid.NewGuid(), "ai-image", "image/v1", "prompt", 1024, 1024, null, null);

    private static WorkflowStepRouteCandidate BuildCandidate(string providerKey, string modelId, int priority) =>
        new(Guid.NewGuid(), providerKey, Guid.NewGuid(), IntegrationConnectionMode.BringYourOwnKey, priority, modelId, null, true);

    private static ResolvedProviderCredential BuildCredential(string providerKey, string model) =>
        new(Guid.Empty, providerKey, IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string> { ["api_key"] = "test", ["executable_path"] = "claude" }, model);
}

file sealed class RouteCapturingWorkflowResolver : IWorkflowConfigurationResolver
{
    private readonly List<WorkflowStepResolutionRequest> _captured;
    private readonly Result<IReadOnlyList<WorkflowStepRouteCandidate>> _result;
    public RouteCapturingWorkflowResolver(List<WorkflowStepResolutionRequest> captured, Result<IReadOnlyList<WorkflowStepRouteCandidate>> result)
    {
        _captured = captured;
        _result = result;
    }
    public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(WorkflowStepResolutionRequest request, CancellationToken cancellationToken = default)
    {
        _captured.Add(request);
        return Task.FromResult(_result);
    }
}

file sealed class RouteCredentialResolver : IIntegrationCredentialResolver
{
    private readonly Result<ResolvedProviderCredential> _result;
    public RouteCredentialResolver(Result<ResolvedProviderCredential> result) => _result = result;
    public Task<Result<ResolvedProviderCredential>> ResolveAsync(Guid organizationId, IntegrationRouteCandidate candidate, CancellationToken cancellationToken = default) => Task.FromResult(_result);
}

file sealed class RouteImageClientFactory : IExternalProviderClientFactory
{
    private readonly IImageModelClient _client;
    public RouteImageClientFactory(IImageModelClient client) => _client = client;
    public TClient Create<TClient>(ResolvedProviderCredential credential) where TClient : notnull => (TClient)_client;
}

file sealed class RouteImageClient : IImageModelClient
{
    private readonly Result<ImageGenerationResult> _result;
    public RouteImageClient(Result<ImageGenerationResult> result) => _result = result;
    public ImageModelCapabilities Capabilities { get; } = new(false, 0);
    public Task<Result<ImageGenerationResult>> GenerateAsync(ImageGenerationRequest request, CancellationToken ct = default) => Task.FromResult(_result);
}

file sealed class RouteCapturingExecutionPolicy : IIntegrationExecutionPolicy
{
    public IReadOnlyList<IntegrationRouteCandidate> Candidates { get; private set; } = [];
    public async Task<Result<ProviderExecutionResult<TResponse>>> ExecuteAsync<TResponse>(IntegrationExecutionRequest request, IReadOnlyList<IntegrationRouteCandidate> candidates, Func<IntegrationRouteCandidate, CancellationToken, Task<Result<TResponse>>> executeAsync, CancellationToken cancellationToken = default) where TResponse : notnull
    {
        Candidates = candidates;
        var result = await executeAsync(candidates[0], cancellationToken);
        return result.IsSuccess
            ? Result.Success(new ProviderExecutionResult<TResponse>(result.Value, candidates[0], []))
            : Result.Failure<ProviderExecutionResult<TResponse>>(result.Error);
    }
}

file sealed class RoutePassthroughExecutionPolicy : IIntegrationExecutionPolicy
{
    public Task<Result<ProviderExecutionResult<TResponse>>> ExecuteAsync<TResponse>(IntegrationExecutionRequest request, IReadOnlyList<IntegrationRouteCandidate> candidates, Func<IntegrationRouteCandidate, CancellationToken, Task<Result<TResponse>>> executeAsync, CancellationToken cancellationToken = default) where TResponse : notnull =>
        Task.FromResult(Result.Failure<ProviderExecutionResult<TResponse>>(IntegrationErrors.ProviderRouteNotConfigured));
}
