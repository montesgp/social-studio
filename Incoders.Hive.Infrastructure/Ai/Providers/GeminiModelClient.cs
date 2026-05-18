using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;

namespace Incoders.Hive.Infrastructure.Ai.Providers;

public sealed class GeminiModelClient : IAiModelClient
{
    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;

    public GeminiModelClient(
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

        using var http = _httpClientFactory.CreateClient(nameof(GeminiModelClient));
        var body = JsonSerializer.Serialize(new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = request.Input }
                    }
                }
            }
        });

        using var response = await http.PostAsync(
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}",
            new StringContent(body, Encoding.UTF8, "application/json"),
            cancellationToken);

        return await ProviderHttpResponseMapper.MapAiResponseAsync(response, "gemini", model, cancellationToken);
    }
}
