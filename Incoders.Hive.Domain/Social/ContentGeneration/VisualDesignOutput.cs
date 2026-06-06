namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Output of the <c>IVisualDesigner</c> Application port.
/// Carries the HTML markup produced by the designer, the prompt template version key,
/// and a flag indicating whether guardrail validation has passed (VD-01, VD-04).
/// </summary>
public sealed record VisualDesignOutput(
    string Markup,
    string PromptVersionKey,
    bool IsValid)
{
    /// <summary>Returns a copy of this output with <see cref="IsValid"/> set to <c>true</c>.</summary>
    public VisualDesignOutput WithValidated() => this with { IsValid = true };
}
