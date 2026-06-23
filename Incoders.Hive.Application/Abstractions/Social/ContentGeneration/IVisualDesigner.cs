using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for the LLM design stage.
/// Implementations translate a <see cref="VisualCompositionSpec"/> and brand context into
/// HTML/CSS markup constrained by the brand's palette, typography, and slot schema (VD-01).
///
/// Contract freeze: all B1b infrastructure types (Playwright, Anthropic SDK, Chromium) are
/// INVISIBLE to this interface — they are wired in the Infrastructure layer only.
///
/// PromptVersionKey convention: implementations must return a non-empty deterministic key
/// identifying the prompt-template version used (e.g. "visual-designer/v1"). This is required
/// even from stub implementations so downstream guardrail and traceability logic can assert it.
///
/// IsActive convention: the pipeline MUST check <see cref="IsActive"/> before calling
/// <see cref="ComposeAsync"/>. When <c>false</c>, the designer+guardrail stage is skipped
/// entirely and processing falls through to the legacy renderer (B1a passthrough behavior).
/// This lets B1a ship a registered no-op without disrupting current rendering; B1b's real
/// AI designer sets <c>IsActive = true</c> and the stage runs normally.
/// </summary>
public interface IVisualDesigner
{
    /// <summary>
    /// Indicates whether this implementation is a live, active designer that should participate
    /// in the designer+guardrail pipeline stage.
    /// <para>
    /// <c>false</c> — passthrough/no-op: the processor MUST skip the designer stage and fall
    /// through to legacy rendering. This is the B1a behavior (stub registered until B1b ships).
    /// </para>
    /// <para>
    /// <c>true</c> — real designer: <see cref="ComposeAsync"/> is called, its failure fails the
    /// format, its output is fed through the guardrail chain before rendering.
    /// </para>
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Produces a <see cref="VisualDesignOutput"/> containing HTML markup for the given spec.
    /// The returned output has <c>IsValid = false</c> until the guardrail pipeline validates it.
    /// Only called when <see cref="IsActive"/> is <c>true</c>.
    /// </summary>
    /// <param name="organizationId">
    /// The organization owning the job. This is PER-JOB context (the same org that owns the
    /// content-generation job) — implementations that call an AI provider MUST thread it into the
    /// provider request so credentials resolve for the correct tenant. It is intentionally a method
    /// parameter, NOT a constructor dependency, because it is unavailable at composition-root startup.
    /// </param>
    /// <param name="aesthetic">
    /// Phase 2 (CAG2-CG-01): optional creatives-first aesthetic context for ClientAesthetic jobs —
    /// the client's real reference creatives (primary signal), optional palette, style descriptors,
    /// motifs, and a variation seed. <c>null</c> means pre-change behavior (brand-manual-driven).
    /// When the palette is empty the designer derives colour from the creatives and MUST NOT
    /// fabricate a default palette.
    /// </param>
    Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken,
        ClientAestheticDesignContext? aesthetic = null);
}
