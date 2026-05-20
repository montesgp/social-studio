using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Infrastructure.Ai.Providers;

internal static class ProviderHttpResponseMapper
{
    public static async Task<Result<AiCompletionResponse>> MapAiResponseAsync(
        HttpResponseMessage response,
        string providerKey,
        string model,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<AiCompletionResponse>(MapStatus(response));
        }

        return Result.Success(new AiCompletionResponse(
            ExtractText(payload),
            providerKey,
            model,
            []));
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

    private static string ExtractText(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content))
        {
            return content.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("content", out var claudeContent)
            && claudeContent.ValueKind == JsonValueKind.Array
            && claudeContent.GetArrayLength() > 0
            && claudeContent[0].TryGetProperty("text", out var text))
        {
            return text.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array
            && candidates.GetArrayLength() > 0
            && candidates[0].TryGetProperty("content", out var geminiContent)
            && geminiContent.TryGetProperty("parts", out var parts)
            && parts.ValueKind == JsonValueKind.Array
            && parts.GetArrayLength() > 0
            && parts[0].TryGetProperty("text", out var geminiText))
        {
            return geminiText.GetString() ?? string.Empty;
        }

        return payload;
    }
}
