using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;

/// <summary>
/// B1a passthrough <see cref="IVisualDesigner"/>: a true no-op that signals the pipeline to skip
/// the designer+guardrail stage entirely and fall through to the existing
/// <c>LocalSvgContentRenderer</c> slot-raster path — preserving current rendering intact
/// until B1b ships the real Opus designer.
///
/// <para>
/// <see cref="IsActive"/> returns <c>false</c>. The processor checks this flag BEFORE calling
/// <see cref="ComposeAsync"/> and, when it is <c>false</c>, bypasses the designer stage without
/// recording any render failure. Flyer and carousel formats therefore render via the legacy path
/// exactly as they did before B1a inserted the designer slot.
/// </para>
///
/// <para>
/// B1b's Infrastructure DI registration (OpusVisualDesigner) REPLACES this via
/// <c>services.AddScoped</c> after the Application layer registers this as a Try-fallback.
/// OpusVisualDesigner sets <c>IsActive = true</c> so the full designer+guardrail chain runs.
/// </para>
/// </summary>
internal sealed class NoOpVisualDesigner : IVisualDesigner
{
    public const string PromptVersionKey = "visual-designer/v1";

    /// <inheritdoc />
    /// <remarks>
    /// Always <c>false</c> for this no-op: the processor MUST skip the designer stage when
    /// this implementation is registered, keeping B1a rendering behavior unchanged.
    /// </remarks>
    public bool IsActive => false;

    /// <remarks>
    /// This method should not be called when <see cref="IsActive"/> is <c>false</c>.
    /// It is retained for interface completeness and returns a clearly-coded failure
    /// to make any accidental call visible in logs.
    /// </remarks>
    public Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken)
    {
        // Should not be called: the processor checks IsActive first.
        // If reached, return a clear failure rather than silently corrupting state.
        return Task.FromResult(
            Result.Failure<VisualDesignOutput>(
                Error.Failure(
                    "visual-designer.no-op",
                    "B1a no-op designer — ComposeAsync was called despite IsActive=false.")));
    }
}
