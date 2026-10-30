namespace SocialStudio.Application.Features.Social.ContentGeneration.Shared;

/// <summary>Overall outcome status for a single format generation run.</summary>
public enum FormatGenerationStatus
{
    Succeeded,
    Partial,
    Failed
}

/// <summary>
/// Strongly-typed result returned by <see cref="IFormatContentGenerator{TPayload}"/>.
/// Carries the generated payload (or null on failure), prompt version, inferred fields,
/// and an optional failure reason.
/// </summary>
/// <typeparam name="TPayload">The format-specific output schema type.</typeparam>
public sealed record FormatGenerationResult<TPayload>(
    FormatGenerationStatus Status,
    TPayload? Payload,
    IReadOnlyList<string> InferredFields,
    string PromptVersion,
    string? FailureReason)
{
    /// <summary>Creates a successful generation result with optional inferred field list.</summary>
    public static FormatGenerationResult<TPayload> Succeeded(
        TPayload payload,
        string promptVersion,
        IReadOnlyList<string> inferredFields) =>
        new(FormatGenerationStatus.Succeeded, payload, inferredFields, promptVersion, null);

    /// <summary>Creates a failed generation result with a reason code.</summary>
    public static FormatGenerationResult<TPayload> Failed(
        string promptVersion,
        string failureReason) =>
        new(FormatGenerationStatus.Failed, default, Array.Empty<string>(), promptVersion, failureReason);

    /// <summary>
    /// Returns a non-generic boxed version suitable for use in the orchestrator's
    /// per-format result dictionary where the exact payload type is not known at compile time.
    /// </summary>
    public FormatGenerationResult<object> AsObject() =>
        new(Status, Payload, InferredFields, PromptVersion, FailureReason);
}
