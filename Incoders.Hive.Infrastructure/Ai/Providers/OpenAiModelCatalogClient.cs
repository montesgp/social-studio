using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;

namespace Incoders.Hive.Infrastructure.Ai.Providers;

/// <summary>
/// Lists the OpenAI models available to the organization's API key via
/// GET https://api.openai.com/v1/models (Authorization: Bearer). Mirrors
/// <see cref="ClaudeModelCatalogClient"/>: models are NEVER hardcoded — the sync reads exactly what the
/// key exposes. <c>/v1/models</c> is a free GET (no token cost), so it is also the cheapest probe.
/// </summary>
internal sealed class OpenAiModelCatalogClient : IAiModelCatalogClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;

    public OpenAiModelCatalogClient(
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

        using var http = _httpClientFactory.CreateClient(nameof(OpenAiModelCatalogClient));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

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
            var decoded = JsonSerializer.Deserialize<OpenAiModelsResponse>(payload, JsonOptions);
            var models = (decoded?.Data ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .Select(x => new AiProviderModel(
                    x.Id.Trim(),
                    x.Id.Trim(),
                    new Dictionary<string, string>
                    {
                        ["created"] = x.Created?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        ["owned_by"] = x.OwnedBy ?? string.Empty,
                    }))
                .OrderBy(x => x.ModelId, StringComparer.OrdinalIgnoreCase)
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

    private sealed record OpenAiModelsResponse(IReadOnlyList<OpenAiModelItem>? Data);

    private sealed record OpenAiModelItem(
        string Id,
        long? Created,
        string? OwnedBy);
}
