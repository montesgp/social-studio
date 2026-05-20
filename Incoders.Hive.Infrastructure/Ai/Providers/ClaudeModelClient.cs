using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;

namespace Incoders.Hive.Infrastructure.Ai.Providers;

public sealed class ClaudeModelClient : IAiModelClient
{
    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;

    public ClaudeModelClient(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<Result<AiCompletionResponse>> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderCredentialReader.TryGet(_credential, "api_key", out var apiKey))
        {
            return Result.Failure<AiCompletionResponse>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        var model = request.PreferredModel ?? _credential.SelectedModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            return Result.Failure<AiCompletionResponse>(IntegrationErrors.AiModelRequired);
        }

        using var http = _httpClientFactory.CreateClient(nameof(ClaudeModelClient));
        http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var body = JsonSerializer.Serialize(new
        {
            model,
            max_tokens = 2048,
            messages = new[]
            {
                new { role = "user", content = request.Input }
            }
        });

        using var response = await http.PostAsync(
            "https://api.anthropic.com/v1/messages",
            new StringContent(body, Encoding.UTF8, "application/json"),
            cancellationToken);

        return await ProviderHttpResponseMapper.MapAiResponseAsync(response, "claude", model, cancellationToken);
    }
}
