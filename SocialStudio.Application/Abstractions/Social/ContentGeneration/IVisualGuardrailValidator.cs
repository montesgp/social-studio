using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for validating <see cref="VisualDesignOutput"/> before rendering.
/// Runs four ordered guardrails (schema → palette → typography → overflow). Schema and overflow
/// are HARD (a failure stops the render); palette and typography are ADVISORY (brand preferences
/// that are logged but never block generation) per the product rule that a story must always be
/// produced even when the AI's design deviates from the brand manual (VD-02, VD-03, VD-04).
/// </summary>
public interface IVisualGuardrailValidator
{
    /// <summary>
    /// Validates <paramref name="output"/> against the brand manual and spec constraints.
    /// Returns a successful result (with <c>IsValid = true</c>) unless a HARD guardrail
    /// (schema or overflow) fails, in which case it carries that violation error. Palette and
    /// typography deviations never fail the result.
    /// </summary>
    Result<VisualDesignOutput> Validate(
        VisualDesignOutput output,
        BrandManualSnapshot brand,
        VisualCompositionSpec spec);
}
