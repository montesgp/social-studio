using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for validating <see cref="VisualDesignOutput"/> before rendering.
/// Runs four ordered guardrails (schema → palette → typography → overflow) and returns
/// a failed <see cref="Result{T}"/> on the first violation — the renderer is never called
/// when a guardrail fails (VD-02, VD-03, VD-04).
/// </summary>
public interface IVisualGuardrailValidator
{
    /// <summary>
    /// Validates <paramref name="output"/> against the brand manual and spec constraints.
    /// Returns a successful result (with <c>IsValid = true</c> set on the output) if all
    /// guardrails pass, or a failed result carrying the first violation error.
    /// </summary>
    Result<VisualDesignOutput> Validate(
        VisualDesignOutput output,
        BrandManualSnapshot brand,
        VisualCompositionSpec spec);
}
