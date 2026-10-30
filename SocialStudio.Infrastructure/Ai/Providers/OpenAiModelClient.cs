using System.Net.Http.Headers;
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

public sealed class OpenAiModelClient : IAiModelClient
{
    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenAiModelClient> _logger;

    public OpenAiModelClient(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAiModelClient>? logger = null)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
        _logger = logger ?? NullLogger<OpenAiModelClient>.Instance;
    }

    public async Task<Result<AiCompletionResponse>> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderCredentialReader.TryGet(_credential, "api_key", out var apiKey))
        {
            _logger.LogWarning(
                "OpenAI completion cannot start because the API key is missing. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey} PromptVersionKey={PromptVersionKey}",
                request.OrganizationId,
                request.CapabilityKey,
                request.PromptVersionKey);

            return Result.Failure<AiCompletionResponse>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        var model = request.PreferredModel ?? _credential.SelectedModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            _logger.LogWarning(
                "OpenAI completion cannot start because the model is missing. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey} PromptVersionKey={PromptVersionKey}",
                request.OrganizationId,
                request.CapabilityKey,
                request.PromptVersionKey);

            return Result.Failure<AiCompletionResponse>(IntegrationErrors.AiModelRequired);
        }

        using var http = _httpClientFactory.CreateClient(nameof(OpenAiModelClient));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var body = JsonSerializer.Serialize(new
        {
            model,
            messages = new[]
            {
                new { role = "user", content = request.Input }
            }
        });

        using var response = await http.PostAsync(
            "https://api.openai.com/v1/chat/completions",
            new StringContent(body, Encoding.UTF8, "application/json"),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "OpenAI completion failed with external status. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey} PromptVersionKey={PromptVersionKey} Model={Model} StatusCode={StatusCode} ReasonPhrase={ReasonPhrase}",
                request.OrganizationId,
                request.CapabilityKey,
                request.PromptVersionKey,
                model,
                (int)response.StatusCode,
                response.ReasonPhrase);
        }

        return await ProviderHttpResponseMapper.MapAiResponseAsync(response, "openai", model, cancellationToken);
    }
}
