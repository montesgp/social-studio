using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai;
using Incoders.Hive.Infrastructure.Integrations;

namespace Incoders.Hive.Tests.Infrastructure.Ai;

public sealed class AiCompletionGatewayTests
{
    [Fact]
    public async Task CompleteJsonAsync_RepairsInvalidJsonOnceBeforeFailingOver()
    {
        var organizationId = Guid.NewGuid();
        var candidate = Candidate("openai");
        var modelClient = new QueueAiModelClient(
            new AiCompletionResponse("not-json", "openai", "gpt-test", []),
            new AiCompletionResponse("{\"value\":\"fixed\"}", "openai", "gpt-test", []));

        var gateway = new AiCompletionGateway(
            new StaticRouteResolver(candidate),
            new StaticCredentialResolver(),
            new StaticClientFactory(modelClient),
            new IntegrationExecutionPolicy(new ProviderFallbackPolicy(), new InMemoryAttemptRepository(), TimeProvider.System));

        var result = await gateway.CompleteJsonAsync<TestPayload>(
            new AiJsonCompletionRequest(
                organizationId,
                "ai-analysis",
                "single-analyzer-v1",
                "{\"input\":\"video\"}",
                PreferredProviderKey: null,
                PreferredModel: "gpt-test"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("fixed", result.Value.Payload.Value);
        Assert.Equal(2, modelClient.Calls);
    }

    private sealed record TestPayload(string Value);

    private static IntegrationRouteCandidate Candidate(string providerKey) =>
        IntegrationRouteCandidate.Create(
            Guid.NewGuid(),
            providerKey,
            IntegrationConnectionMode.BringYourOwnKey,
            priority: 1,
            allowManagedFallback: true,
            maxAttempts: 1,
            timeoutMs: 2_000,
            selectedModel: "gpt-test",
            isEnabled: true).Value;

    private sealed class QueueAiModelClient : IAiModelClient
    {
        private readonly Queue<AiCompletionResponse> _responses;

        public QueueAiModelClient(params AiCompletionResponse[] responses)
        {
            _responses = new Queue<AiCompletionResponse>(responses);
        }

        public int Calls { get; private set; }

        public Task<Result<AiCompletionResponse>> CompleteAsync(
            AiCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Result.Success(_responses.Dequeue()));
        }
    }

    private sealed class StaticRouteResolver : IOrganizationIntegrationResolver
    {
        private readonly IntegrationRouteCandidate _candidate;

        public StaticRouteResolver(IntegrationRouteCandidate candidate)
        {
            _candidate = candidate;
        }

        public Task<Result<IntegrationRouteResolution>> ResolveRouteAsync(
            IntegrationRouteRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(new IntegrationRouteResolution([_candidate], [])));
    }

    private sealed class StaticCredentialResolver : IIntegrationCredentialResolver
    {
        public Task<Result<ResolvedProviderCredential>> ResolveAsync(
            Guid organizationId,
            IntegrationRouteCandidate candidate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(new ResolvedProviderCredential(
                candidate.ProviderId,
                candidate.ProviderKey,
                candidate.ConnectionMode,
                new Dictionary<string, string> { ["api_key"] = "test" },
                candidate.SelectedModel)));
    }

    private sealed class StaticClientFactory : IExternalProviderClientFactory
    {
        private readonly IAiModelClient _client;

        public StaticClientFactory(IAiModelClient client)
        {
            _client = client;
        }

        public TClient Create<TClient>(ResolvedProviderCredential credential)
            where TClient : notnull =>
            (TClient)_client;
    }

    private sealed class InMemoryAttemptRepository : IIntegrationExecutionAttemptRepository
    {
        public Task AddAsync(IntegrationExecutionAttemptRecord attempt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<IntegrationExecutionAttemptRecord>> ListByAnalysisIdAsync(
            Guid analysisId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IntegrationExecutionAttemptRecord>>([]);
    }
}
