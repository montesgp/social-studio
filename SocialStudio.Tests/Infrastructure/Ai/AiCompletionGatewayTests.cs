using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Ai;

namespace SocialStudio.Tests.Infrastructure.Ai;

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

    [Fact]
    public async Task CompleteJsonAsync_ParsesFencedJson_WithoutRepair()
    {
        var modelClient = new QueueAiModelClient(
            new AiCompletionResponse("Here is the result:\n```json\n{\"value\":\"fenced\"}\n```", "openai", "gpt-test", []));

        var gateway = new AiCompletionGateway(
            new StaticIntegrationResolver("openai"),
            new StaticClientFactory(modelClient));

        var result = await gateway.CompleteJsonAsync<TestPayload>(
            new AiJsonCompletionRequest(
                Guid.NewGuid(), null, "ai-analysis", "single-analyzer-v1", "{}",
                PreferredProviderKey: "openai",
                PreferredModel: "gpt-test"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("fenced", result.Value.Payload.Value);
        Assert.Equal(1, modelClient.Calls); // fenced JSON parsed directly, no repair call needed
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
                SelectedAiModel: "gpt-test",
                new Dictionary<string, string> { ["api_key"] = "test" })));
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

        public bool Supports<TClient>(string providerKey)
            where TClient : notnull => true;
    }
}
