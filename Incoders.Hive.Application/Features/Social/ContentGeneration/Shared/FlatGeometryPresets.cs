namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// The resolved pixel geometry for one <see cref="DecorationMotifs.CompositionArchetype"/> preset,
/// covering every deterministic flat-carousel slot the archetype repositions (CAVS-CG-02). Canvas
/// reference: 1080×1350. <see cref="LeftAligned"/> reproduces today's hardcoded literals exactly —
/// the byte-identity cold-start contract (CAVS-CG-03) depends on every one of its values matching
/// the pre-change builder verbatim.
/// </summary>
public sealed record FlatCarouselGeometry(
    int AccentLeft,
    int AccentTop,
    int TitleLeft,
    int TitleTop,
    int TitleWidth,
    string TitleAlign,
    int DividerLeft,
    int DividerTop,
    int DividerWidth,
    int SubtitleLeft,
    int SubtitleTop,
    int SubtitleWidth,
    string SubtitleAlign,
    int CtaLeft,
    int CtaBottom,
    int CtaWidth,
    string CtaAlign,
    int? OrnamentLeft,
    int? OrnamentRight,
    int OrnamentTop,
    int OrnamentWidth,
    int OrnamentHeight);

/// <summary>
/// Token → <see cref="FlatCarouselGeometry"/> lookup for the deterministic flat-carousel builder
/// (CAVS-CG-02, design D1 / geometry table). This is the ONLY place a
/// <see cref="DecorationMotifs.CompositionArchetype"/> token is recognised — everywhere else the
/// token is an open, unrecognised-preserving string (<see cref="CompositionArchetypes.Normalize"/>).
/// The catch-all <c>_ =&gt;</c> arm below returns <see cref="LeftAligned"/> for any unrecognised or
/// novel token, so teaching the renderer a new archetype later is one new const plus one new preset
/// row — never a schema or data migration.
/// </summary>
public static class FlatGeometryPresets
{
    /// <summary>Identity preset — reproduces today's hardcoded flat-carousel geometry exactly.</summary>
    public static readonly FlatCarouselGeometry LeftAligned = new(
        AccentLeft: 104,
        AccentTop: 180,
        TitleLeft: 104,
        TitleTop: 235,
        TitleWidth: 872,
        TitleAlign: "left",
        DividerLeft: 104,
        DividerTop: 492,
        DividerWidth: 872,
        SubtitleLeft: 108,
        SubtitleTop: 540,
        SubtitleWidth: 815,
        SubtitleAlign: "left",
        CtaLeft: 108,
        CtaBottom: 220,
        CtaWidth: 760,
        CtaAlign: "left",
        OrnamentLeft: null,
        OrnamentRight: 96,
        OrnamentTop: 64,
        OrnamentWidth: 160,
        OrnamentHeight: 140);

    public static readonly FlatCarouselGeometry Centered = new(
        AccentLeft: 456,
        AccentTop: 180,
        TitleLeft: 104,
        TitleTop: 235,
        TitleWidth: 872,
        TitleAlign: "center",
        DividerLeft: 450,
        DividerTop: 492,
        DividerWidth: 872,
        SubtitleLeft: 132,
        SubtitleTop: 540,
        SubtitleWidth: 816,
        SubtitleAlign: "center",
        CtaLeft: 160,
        CtaBottom: 220,
        CtaWidth: 760,
        CtaAlign: "center",
        OrnamentLeft: 460,
        OrnamentRight: null,
        OrnamentTop: 40,
        OrnamentWidth: 160,
        OrnamentHeight: 140);

    public static readonly FlatCarouselGeometry LowerThird = new(
        AccentLeft: 104,
        AccentTop: 620,
        TitleLeft: 104,
        TitleTop: 675,
        TitleWidth: 872,
        TitleAlign: "left",
        DividerLeft: 104,
        DividerTop: 930,
        DividerWidth: 872,
        SubtitleLeft: 108,
        // Adjusted from the design's proposed 960 to 940: at the subtitle's 310px min-height, top:960
        // would overlap the brand-mark's bottom:56px/36px-tall anchor box (design.md explicitly marks
        // non-identity pixel values as proposals subject to the apply-time overlap assertion, WU-5
        // task 5.4, CAVS-CG-03-b — the assertion is the binding constraint, not the literal number).
        SubtitleTop: 940,
        SubtitleWidth: 815,
        SubtitleAlign: "left",
        CtaLeft: 108,
        CtaBottom: 120,
        CtaWidth: 760,
        CtaAlign: "left",
        OrnamentLeft: null,
        OrnamentRight: 96,
        OrnamentTop: 64,
        OrnamentWidth: 160,
        OrnamentHeight: 140);

    /// <summary>
    /// Resolves a raw (or already-normalised) archetype token to its geometry preset. Null, blank,
    /// or any token not matching one of the recognised v1 archetypes
    /// (<see cref="CompositionArchetypes.LeftAligned"/>, <see cref="CompositionArchetypes.Centered"/>,
    /// <see cref="CompositionArchetypes.LowerThird"/>) resolves to <see cref="LeftAligned"/> — the
    /// identity preset — never throws (CAVS-CG-02-c/d).
    /// </summary>
    public static FlatCarouselGeometry Resolve(string? token) =>
        CompositionArchetypes.Normalize(token) switch
        {
            CompositionArchetypes.Centered => Centered,
            CompositionArchetypes.LowerThird => LowerThird,
            _ => LeftAligned,
        };
}
