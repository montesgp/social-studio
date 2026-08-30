using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ContentGeneration;

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
            [],
            ExtractTokenUsage(payload)));
    }

    /// <summary>
    /// Maps a successful provider HTTP response when the payload has already been read.
    /// Lets the caller log the body once (for diagnostics) without reading the content twice.
    /// </summary>
    public static Result<AiCompletionResponse> MapAiSuccess(string payload, string providerKey, string model) =>
        Result.Success(new AiCompletionResponse(
            ExtractText(payload),
            providerKey,
            model,
            [],
            ExtractTokenUsage(payload)));

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
            && claudeContent.GetArrayLength() > 0)
        {
            // Tool Use response (Claude 4.x JSON-mode): find the tool_use block and return its
            // `input` serialized as JSON. The downstream parser deserializes it into TResponse.
            foreach (var block in claudeContent.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var blockType)
                    && blockType.ValueKind == JsonValueKind.String
                    && string.Equals(blockType.GetString(), "tool_use", StringComparison.Ordinal)
                    && block.TryGetProperty("input", out var input))
                {
                    return input.GetRawText();
                }
            }

            // Fallback: text block (legacy / non-tool-use responses).
            if (claudeContent[0].TryGetProperty("text", out var text))
            {
                return text.GetString() ?? string.Empty;
            }
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

    /// <summary>
    /// Extracts token usage from provider-native response metadata.
    /// Returns null when no usage block is present (tokens-missing semantics).
    /// Handles:
    ///   - Claude: usage.input_tokens / usage.output_tokens
    ///   - OpenAI: usage.prompt_tokens / usage.completion_tokens
    ///   - Gemini: usageMetadata.promptTokenCount / usageMetadata.candidatesTokenCount
    /// </summary>
    private static TokenUsage? ExtractTokenUsage(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            // Claude: { "usage": { "input_tokens": N, "output_tokens": N } }
            if (root.TryGetProperty("usage", out var claudeUsage)
                && claudeUsage.ValueKind == JsonValueKind.Object
                && claudeUsage.TryGetProperty("input_tokens", out var inputTokens)
                && claudeUsage.TryGetProperty("output_tokens", out var outputTokens))
            {
                return new TokenUsage(inputTokens.GetInt32(), outputTokens.GetInt32());
            }

            // OpenAI: { "usage": { "prompt_tokens": N, "completion_tokens": N } }
            if (root.TryGetProperty("usage", out var openAiUsage)
                && openAiUsage.ValueKind == JsonValueKind.Object
                && openAiUsage.TryGetProperty("prompt_tokens", out var promptTokens)
                && openAiUsage.TryGetProperty("completion_tokens", out var completionTokens))
            {
                return new TokenUsage(promptTokens.GetInt32(), completionTokens.GetInt32());
            }

            // Gemini: { "usageMetadata": { "promptTokenCount": N, "candidatesTokenCount": N } }
            if (root.TryGetProperty("usageMetadata", out var geminiUsage)
                && geminiUsage.ValueKind == JsonValueKind.Object
                && geminiUsage.TryGetProperty("promptTokenCount", out var promptTokenCount)
                && geminiUsage.TryGetProperty("candidatesTokenCount", out var candidatesTokenCount))
            {
                return new TokenUsage(promptTokenCount.GetInt32(), candidatesTokenCount.GetInt32());
            }
        }
        catch (JsonException)
        {
            // Malformed payload — treat as tokens-missing
        }

        return null;
    }
}
