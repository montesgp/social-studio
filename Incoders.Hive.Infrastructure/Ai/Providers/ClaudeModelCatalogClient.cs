using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;

namespace Incoders.Hive.Infrastructure.Ai.Providers;

internal sealed class ClaudeModelCatalogClient : IAiModelCatalogClient
{
    private const string AnthropicVersion = "2023-06-01";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;

    public ClaudeModelCatalogClient(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<Result<IReadOnlyList<AiProviderModel>>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!ProviderCredentialReader.TryGet(_credential, "api_key", out var apiKey))
        {
            return Result.Failure<IReadOnlyList<AiProviderModel>>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        using var http = _httpClientFactory.CreateClient(nameof(ClaudeModelCatalogClient));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<IReadOnlyList<AiProviderModel>>(IntegrationErrors.ProviderTimeout);
        }
        catch (HttpRequestException)
        {
            return Result.Failure<IReadOnlyList<AiProviderModel>>(IntegrationErrors.ExternalProviderUnavailable);
        }

        using var responseHandle = response;
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<IReadOnlyList<AiProviderModel>>(MapStatus(response));
        }

        try
        {
            var decoded = JsonSerializer.Deserialize<ClaudeModelsResponse>(payload, JsonOptions);
            var models = (decoded?.Data ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .Select(x => new AiProviderModel(
                    x.Id.Trim(),
                    string.IsNullOrWhiteSpace(x.DisplayName) ? x.Id.Trim() : x.DisplayName.Trim(),
                    new Dictionary<string, string>
                    {
                        ["created_at"] = x.CreatedAt ?? string.Empty,
                        ["type"] = x.Type ?? string.Empty,
                    }))
                .ToList();

            return Result.Success<IReadOnlyList<AiProviderModel>>(models);
        }
        catch (JsonException)
        {
            return Result.Failure<IReadOnlyList<AiProviderModel>>(IntegrationErrors.ExternalProviderUnavailable);
        }
    }

    private static Error MapStatus(HttpResponseMessage response) =>
        (int)response.StatusCode switch
        {
            401 or 403 => IntegrationErrors.ProviderCredentialUnavailable,
            408 => IntegrationErrors.ProviderTimeout,
            429 => IntegrationErrors.ProviderRateLimited,
            >= 500 => IntegrationErrors.ExternalProviderUnavailable,
            _ => IntegrationErrors.ExternalProviderUnavailable,
        };

    private sealed record ClaudeModelsResponse(IReadOnlyList<ClaudeModelItem>? Data);

    private sealed record ClaudeModelItem(
        string Id,
        string? Type,
        string? DisplayName,
        string? CreatedAt);
}

