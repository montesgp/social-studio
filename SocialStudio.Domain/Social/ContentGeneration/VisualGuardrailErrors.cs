using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.ContentGeneration;

/// <summary>
/// Typed error factories for the visual guardrail pipeline (VD-02, VD-03, VD-04).
/// Errors are defined in Domain so both Application and test projects can reference them
/// without introducing Infrastructure dependencies.
/// </summary>
public static class VisualGuardrailErrors
{
    /// <summary>The designer markup does not conform to the constrained HTML schema (VD-04).</summary>
    public static readonly Error InvalidMarkup =
        Error.Validation(
            "visual-guardrail.invalid-markup",
            "visual-guardrail.errors.invalid-markup");

    /// <summary>A color token in the markup is not in the brand palette (VD-02).</summary>
    public static Error OffPalette(string color) =>
        Error.Validation(
            "visual-guardrail.off-palette",
            $"Color '{color}' is not in the brand palette.");

    /// <summary>A font-family in the markup is not in the brand typography allow-list (VD-02).</summary>
    public static Error FontNotAllowed(string family) =>
        Error.Validation(
            "visual-guardrail.font-not-allowed",
            $"Font-family '{family}' is not in the brand typography allow-list.");

    /// <summary>A text slot exceeds its declared maximum lines/height (VD-03).</summary>
    public static Error TextOverflow(string slot) =>
        Error.Validation(
            "visual-guardrail.text-overflow",
            $"Text in slot '{slot}' overflows the declared constraint.");
}
