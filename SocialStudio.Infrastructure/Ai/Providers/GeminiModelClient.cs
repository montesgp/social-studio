using System.Text;
using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Infrastructure.Ai.Providers;

public sealed class GeminiModelClient : IAiModelClient
{
    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GeminiModelClient> _logger;

    public GeminiModelClient(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory,
        ILogger<GeminiModelClient>? logger = null)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
        _logger = logger ?? NullLogger<GeminiModelClient>.Instance;
    }

    public async Task<Result<AiCompletionResponse>> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderCredentialReader.TryGet(_credential, "api_key", out var apiKey))
        {
            _logger.LogWarning(
                "Gemini completion cannot start because the API key is missing. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey} PromptVersionKey={PromptVersionKey}",
                request.OrganizationId,
                request.CapabilityKey,
                request.PromptVersionKey);

            return Result.Failure<AiCompletionResponse>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        var model = request.PreferredModel ?? _credential.SelectedModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            _logger.LogWarning(
                "Gemini completion cannot start because the model is missing. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey} PromptVersionKey={PromptVersionKey}",
                request.OrganizationId,
                request.CapabilityKey,
                request.PromptVersionKey);

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

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Gemini completion failed with external status. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey} PromptVersionKey={PromptVersionKey} Model={Model} StatusCode={StatusCode} ReasonPhrase={ReasonPhrase}",
                request.OrganizationId,
                request.CapabilityKey,
                request.PromptVersionKey,
                model,
                (int)response.StatusCode,
                response.ReasonPhrase);
        }

        return await ProviderHttpResponseMapper.MapAiResponseAsync(response, "gemini", model, cancellationToken);
    }
}
