namespace SocialStudio.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Non-generic marker interface allowing the orchestrator to work with a heterogeneous
/// collection of format generators without knowing the concrete payload type at compile time.
/// Exposes the boxed generate method plus key/version for registry lookups.
/// </summary>
public interface IFormatContentGenerator
{
    /// <summary>Unique key identifying this format (e.g. "carousel", "story").</summary>
    string FormatKey { get; }

    /// <summary>Prompt version constant associated with this handler.</summary>
    string PromptVersion { get; }

    /// <summary>
    /// Generates content for this format, returning a boxed (non-generic) result.
    /// Used by the orchestrator which aggregates results from multiple typed generators.
    /// </summary>
    Task<FormatGenerationResult<object>> GenerateBoxedAsync(
        GenerationContext ctx,
        CancellationToken cancellationToken);
}

/// <summary>
/// Typed format content generator. Implement this interface for each supported format.
/// Also implements <see cref="IFormatContentGenerator"/> via the adapter methods
/// so a single registration covers both the typed and the untyped usage.
/// </summary>
/// <typeparam name="TPayload">The format-specific structured output schema type.</typeparam>
public interface IFormatContentGenerator<TPayload> : IFormatContentGenerator
    where TPayload : notnull
{
    /// <summary>Generates content for this format and returns a strongly-typed result.</summary>
    Task<FormatGenerationResult<TPayload>> GenerateAsync(
        GenerationContext ctx,
        CancellationToken cancellationToken);
}
