using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Ai.Providers;

public sealed class ClaudeModelClient : IAiModelClient
{
    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClaudeModelClient> _logger;

    public ClaudeModelClient(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory,
        ILogger<ClaudeModelClient>? logger = null)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
        _logger = logger ?? NullLogger<ClaudeModelClient>.Instance;
    }

    public async Task<Result<AiCompletionResponse>> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderCredentialReader.TryGet(_credential, "api_key", out var apiKey))
        {
            _logger.LogWarning(
                "Claude completion cannot start because the API key is missing. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey} PromptVersionKey={PromptVersionKey}",
                request.OrganizationId,
                request.CapabilityKey,
                request.PromptVersionKey);

            return Result.Failure<AiCompletionResponse>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        var model = request.PreferredModel ?? _credential.SelectedModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            _logger.LogWarning(
                "Claude completion cannot start because the model is missing. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey} PromptVersionKey={PromptVersionKey}",
                request.OrganizationId,
                request.CapabilityKey,
                request.PromptVersionKey);

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

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(
                "https://api.anthropic.com/v1/messages",
                new StringContent(body, Encoding.UTF8, "application/json"),
                cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Claude completion timed out before the response returned. OrganizationId={OrganizationId} Model={Model}",
                request.OrganizationId,
                model);

            return Result.Failure<AiCompletionResponse>(IntegrationErrors.ProviderTimeout);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Claude completion could not reach the provider. OrganizationId={OrganizationId} Model={Model}",
                request.OrganizationId,
                model);

            return Result.Failure<AiCompletionResponse>(IntegrationErrors.ExternalProviderUnavailable);
        }

        using var responseHandle = response;

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Claude completion failed with external status. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey} PromptVersionKey={PromptVersionKey} Model={Model} StatusCode={StatusCode} ReasonPhrase={ReasonPhrase}",
                request.OrganizationId,
                request.CapabilityKey,
                request.PromptVersionKey,
                model,
                (int)response.StatusCode,
                response.ReasonPhrase);
        }

        return await ProviderHttpResponseMapper.MapAiResponseAsync(response, "claude", model, cancellationToken);
    }
}
