namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Application value object representing the aesthetic profile of a client, derived from the
/// most recent completed profile analysis.
///
/// <para>
/// Phase 1 fields:
/// <list type="bullet">
/// <item><see cref="BackgroundDescriptors"/> — DetectedBackgroundImages text entries from the AI analysis.</item>
/// <item><see cref="VoiceTone"/> — direct-read copy-style signal from the AI analysis (OQ-3).</item>
/// <item><see cref="AverageSlidesPerCarousel"/> — server-derived stat; mean carousel child count (CAG-PA-02).</item>
/// <item><see cref="AnalyzedAtUtc"/> — CompletedAtUtc of the source analysis row; freshness provenance.</item>
/// </list>
/// </para>
///
/// <para>
/// Phase 2 (additive, reserved): a nullable <c>IReadOnlyList&lt;string&gt;? BackgroundImageUrls</c>
/// field will be added as an optional trailing parameter when Phase 2 ships, with a null default
/// so all existing construction sites remain unaffected.
/// </para>
///
/// <para>
/// A <c>null</c> return from <see cref="Abstractions.Social.ContentGeneration.IClientAestheticProfileReader"/>
/// means no completed analysis exists — callers degrade to Creative + fire a background refresh.
/// An instance with empty <see cref="BackgroundDescriptors"/> and non-null <see cref="AnalyzedAtUtc"/>
/// means an analysis exists but found no descriptors; VoiceTone/avg-slides may still be present.
/// </para>
///
/// Refs: design §3.1; CAG-CG-05.
/// No Infrastructure types — this is a pure Application value object.
/// </summary>
public sealed record ClientAestheticProfile(
    IReadOnlyList<string> BackgroundDescriptors,
    string? VoiceTone,
    double? AverageSlidesPerCarousel,
    DateTime? AnalyzedAtUtc,
    // Phase 2 (CAG2-PA-03/04) — additive, optional. All default to null so every existing Phase 1
    // construction site and `with` expression compiles and behaves unchanged.
    //
    // Palette is OPTIONAL: an empty/null palette is a valid result (a client without a marked
    // palette); never treat it as a degrade and never fabricate a default palette from its absence.
    IReadOnlyList<AestheticPaletteEntry>? Palette = null,
    // Free-text visual style/mood/composition descriptors extracted from the client's posts.
    IReadOnlyList<string>? StyleDescriptors = null,
    // Recurring visual motifs (e.g. "hand-drawn arrows", "film-grain overlay").
    IReadOnlyList<string>? Motifs = null,
    // Caption-derived voice/copy style, distinct from the Phase 1 VoiceTone narrative signal.
    string? CaptionVoice = null,
    // Durable references to the client's real post images — the PRIMARY (creatives-first) signal.
    IReadOnlyList<Domain.Social.ProfileAnalysis.ProfilePostImageRef>? ReferenceImages = null);
