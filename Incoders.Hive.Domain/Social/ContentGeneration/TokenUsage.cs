namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Captures token counts from an AI provider response.
/// Both fields are nullable to support providers that omit usage metadata (tokens-missing).
/// </summary>
public sealed record TokenUsage(int? InputTokens, int? OutputTokens)
{
    /// <summary>Represents a response where the provider returned no token usage information.</summary>
    public static TokenUsage Missing { get; } = new(null, null);
}
