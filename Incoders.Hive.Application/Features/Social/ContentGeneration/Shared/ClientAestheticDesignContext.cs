using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// The creatives-first aesthetic bundle passed to the AI visual designer for a ClientAesthetic job.
/// Phase 2 (CAG2-CG-01). Pure Application value object — no provider/vision SDK types.
/// </summary>
/// <remarks>
/// Built from the enriched <see cref="ClientAestheticProfile"/> at generation time. The
/// <see cref="ReferenceImages"/> are the PRIMARY signal (the client's real creatives); the palette
/// is OPTIONAL and may be empty — when empty the designer derives colour freely from the reference
/// creatives and MUST NOT fabricate a default palette. <see cref="VariationSeed"/> drives
/// variety-within-coherence across a multi-asset package (e.g. seeded from the job id).
/// </remarks>
/// <param name="ReferenceImages">Durable references to the client's real post images (capped per job). Primary signal.</param>
/// <param name="Palette">Optional role-ordered palette; empty/null when the client has no marked palette.</param>
/// <param name="StyleDescriptors">Visual style/mood/composition descriptors.</param>
/// <param name="Motifs">Recurring visual motifs.</param>
/// <param name="VariationSeed">Stable seed for reproducible composition variety within the client's style.</param>
/// <param name="BrandMark">
/// Recurring brand mark detected across the client's corpus (faithful-flat-slide-rendering;
/// FFS-CG-02). Additive trailing nullable — null when no mark was reliably detected (cold-start
/// parity; FFS-CG-03-a). The AI designer MUST NEVER draw/place a mark itself — it is placed
/// deterministically by the renderer as a separate editable element.
/// </param>
public sealed record ClientAestheticDesignContext(
    IReadOnlyList<ProfilePostImageRef> ReferenceImages,
    IReadOnlyList<AestheticPaletteEntry>? Palette,
    IReadOnlyList<string> StyleDescriptors,
    IReadOnlyList<string> Motifs,
    string VariationSeed,
    BrandMarkDetection? BrandMark = null,
    // client-driven-decoration (additive trailing nullable): measured photo/flat/decorator mix from
    // the client's corpus. Null when never measured → the designer keeps its default direction.
    ImageTypeRatio? ImageTypeRatio = null)
{
    /// <summary>
    /// A decorator share at or below this is treated as noise, not a style — one stray ornamented
    /// slide in a large corpus does not make a client a decorator.
    /// </summary>
    private const double DecoratorNoiseFloor = 0.05;

    /// <summary>True when an actual colour palette is available; false when the client has none (free-derive colour).</summary>
    public bool HasPalette => Palette is { Count: > 0 };

    /// <summary>
    /// Whether the designer may add decorative shapes, blobs and glows.
    ///
    /// <para>
    /// True unless the client's corpus was actually MEASURED and shows they do not decorate. An
    /// unmeasured client (null ratio, or a zero-slide sample, which proves nothing) keeps the
    /// default design direction, so this can only ever remove decoration from a client we have
    /// positive evidence about.
    /// </para>
    /// </summary>
    public bool AllowsDecoration =>
        ImageTypeRatio is not { SampleSize: > 0 } measured || measured.Decorator > DecoratorNoiseFloor;
}
