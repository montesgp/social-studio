using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai;

namespace Incoders.Hive.Tests.Infrastructure.Ai;

public sealed class AiCompletionGatewayTests
{
    [Fact]
    public async Task CompleteJsonAsync_RepairsInvalidJsonOnceBeforeFailing()
    {
        var organizationId = Guid.NewGuid();
        var modelClient = new QueueAiModelClient(
            new AiCompletionResponse("not-json", "openai", "gpt-test", []),
            new AiCompletionResponse("{\"value\":\"fixed\"}", "openai", "gpt-test", []));

        var gateway = new AiCompletionGateway(
            new StaticIntegrationResolver("openai"),
            new StaticClientFactory(modelClient));

        var result = await gateway.CompleteJsonAsync<TestPayload>(
            new AiJsonCompletionRequest(
                organizationId,
                null,
                "ai-analysis",
                "single-analyzer-v1",
                "{\"input\":\"video\"}",
                PreferredProviderKey: "openai",
                PreferredModel: "gpt-test"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("fixed", result.Value.Payload.Value);
        Assert.Equal("openai", result.Value.ProviderKey);
        Assert.Equal(2, modelClient.Calls);
    }

    [Fact]
    public async Task CompleteJsonAsync_FailsWhenNoProviderKey()
    {
        var gateway = new AiCompletionGateway(
            new StaticIntegrationResolver("openai"),
            new StaticClientFactory(new QueueAiModelClient()));

        var result = await gateway.CompleteJsonAsync<TestPayload>(
            new AiJsonCompletionRequest(
                Guid.NewGuid(), null, "ai-analysis", "single-analyzer-v1", "{}",
                PreferredProviderKey: null,
                PreferredModel: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.DefaultAiProviderNotConnected.Code, result.Error.Code);
    }

    private sealed record TestPayload(string Value);

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

    private sealed class StaticIntegrationResolver : IIntegrationResolver
    {
        private readonly string _providerKey;

        public StaticIntegrationResolver(string providerKey) => _providerKey = providerKey;

        public Task<Result<ResolvedIntegration>> ResolveProviderAsync(
            Guid organizationId,
            string providerKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(new ResolvedIntegration(
                organizationId,
                _providerKey,
                _providerKey,
                IntegrationProviderCategory.AiModel,
                IntegrationConnectionMode.BringYourOwnKey,
                IntegrationStatus.Connected,
                IsReadOnly: false,
                IsDefaultAi: true,
                SelectedAiModel: "gpt-test",
                new Dictionary<string, string> { ["api_key"] = "test" })));

        public Task<Result<ResolvedIntegration>> ResolveDefaultAiProviderAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            ResolveProviderAsync(organizationId, _providerKey, cancellationToken);
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
}
