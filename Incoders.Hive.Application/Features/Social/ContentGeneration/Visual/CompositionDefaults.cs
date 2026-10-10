using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;
using Incoders.Hive.Domain.Social.ContentFormats;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;

/// <summary>
/// Default palette and typography applied when brand data is missing or incomplete.
/// Neutral grays + accent blue as per WU4 design decision.
/// </summary>
internal static class CompositionDefaults
{
    internal static readonly CompositionPalette Palette = new(
        Primary: "#1F2937",    // dark gray
        Secondary: "#374151",  // medium gray
        Accent: "#3B82F6",     // accent blue
        Text: "#111827",       // near-black text
        Background: "#F9FAFB" // off-white background
    );

    /// <summary>
    /// Bundled OFL substitute for the legacy "Arial" default (Decision #1169 — Content Studio editing
    /// completeness). Arial is Monotype-licensed and cannot be legally embedded, and with no
    /// Dockerfile provisioning fonts in this repository the render host's font state is unknown, so
    /// the fallback MUST be a font this assembly actually bundles (see
    /// <c>Fonts.FontCatalog.SystemSafeList</c> and <c>EmbeddedFontProvider</c>).
    /// </summary>
    private const string FallbackFontFamily = "Liberation Sans";

    internal static readonly CompositionTypography Typography = new(
        HeadingFamily: FallbackFontFamily,
        HeadingSize: 28,
        BodyFamily: FallbackFontFamily,
        BodySize: 16);

    internal static readonly CompositionDimensions StandardDimensions = new(1080, 1080, 96);

    /// <summary>
    /// client-aesthetic-fidelity-fixes (WU-3b, D5): flat opacity applied to a real photo `&lt;img&gt;`
    /// when the resolved per-slide <see cref="SlideBackgroundTransparency.Blended"/> signal is true,
    /// composited over a solid palette-derived backdrop. The only render mechanism that is
    /// deterministic, direction-free, and stacking-context-independent under headless Chromium
    /// screenshotting. Design-chosen, not measured — a single named constant so a visual review can
    /// retune it without a contract change.
    /// </summary>
    internal const double BlendedPhotoOpacity = 0.72;

    /// <summary>
    /// Feed carousel slides use the modern 4:5 feed ratio (1080×1350) — more screen real-estate in the
    /// Instagram feed than the legacy 1:1 square. Stories use 9:16; text cards stay square.
    /// </summary>
    internal static readonly CompositionDimensions CarouselDimensions = new(1080, 1350, 96);

    /// <summary>
    /// Story canvas — full-screen 9:16 (1080×1920). Single stories and story-carousel slides both use
    /// this so the editable design fills the story frame instead of leaving dead space.
    /// </summary>
    internal static readonly CompositionDimensions StoryDimensions = new(1080, 1920, 96);

    /// <summary>
    /// Resolves the logical canvas dimensions for a format key using the SAME Surface/Layout rule the
    /// visual composers apply (see <c>SurfaceScopedCarouselVisualComposer.Dimensions</c>), so any
    /// caller that needs the canvas size before a <see cref="VisualCompositionSpec"/> exists — e.g. an
    /// image-generation request that must be sized to avoid an upscale at render time (CAC-IMG-04) —
    /// derives it from the SAME single source of truth instead of hardcoding a second magic constant.
    /// Falls back to <see cref="StandardDimensions"/> (1:1) when the format key does not resolve.
    /// </summary>
    internal static CompositionDimensions ResolveDimensionsForFormat(string formatKey)
    {
        var definition = FormatCatalog.ResolveForAssembly(formatKey);
        if (definition is null)
        {
            return StandardDimensions;
        }

        // Fully qualified: the sibling `Visual.Layout` namespace (WU-2, SlotOverlapDetector)
        // would otherwise shadow the Domain `Layout` enum for unqualified lookups here.
        if (definition.Layout == Incoders.Hive.Domain.Social.ContentFormats.Layout.Carousel)
        {
            return definition.Surface == Surface.Story ? StoryDimensions : CarouselDimensions;
        }

        return definition.Surface == Surface.Story ? StoryDimensions : StandardDimensions;
    }

    /// <summary>
    /// Resolves a brand palette or returns defaults and marks ColorPalette as inferred.
    /// </summary>
    internal static CompositionPalette ResolvePalette(BrandSnapshot brand, InferenceAccumulator inferred)
    {
        var colors = brand.Brand?.ColorPalette
            .Where(wc => !string.IsNullOrWhiteSpace(wc.Hex))
            .Select(wc => wc.Hex.Trim())
            .ToArray() ?? [];

        var hasPalette = colors.Length > 0;
        inferred.Track("brand.ColorPalette", !hasPalette);

        if (!hasPalette)
        {
            return Palette;
        }

        return new CompositionPalette(
            Primary: Pick(colors, 0, Palette.Primary),
            Secondary: Pick(colors, 1, Palette.Secondary),
            Accent: Pick(colors, 2, Palette.Accent),
            Text: Pick(colors, 3, Palette.Text),
            Background: Pick(colors, 4, Palette.Background));
    }

    /// <summary>
    /// Resolves a palette using the client's ranked rotating colour pairings when present,
    /// otherwise delegates to the existing positional <see cref="ResolvePalette(BrandSnapshot, InferenceAccumulator)"/>.
    ///
    /// Rotation formula: <c>pairings[(slideIndex-1) % pairings.Count]</c>.
    /// When <paramref name="profile"/> is null or <c>ColorPairings</c> is null/empty, the result is
    /// byte-identical to <see cref="ResolvePalette(BrandSnapshot, InferenceAccumulator)"/> (cold-start parity).
    ///
    /// Refs: CAC-CG-02; design §3.3, §0 OQ-2.
    /// </summary>
    internal static CompositionPalette ResolvePalette(
        BrandSnapshot brand,
        ClientAestheticProfile? profile,
        int slideIndex,
        InferenceAccumulator inferred,
        // client-style-fidelity CSF-CG-03: carousel length and this slide's background kind, so
        // observed pairings can be allocated by how often the client really uses each one.
        int? totalSlides = null,
        SlideBackgroundKind? slideKind = null)
    {
        var pairings = profile?.ColorPairings;
        if (pairings is { Count: > 0 })
        {
            var p = pairings.Any(x => x.IsObserved)
                ? SelectObservedPairing(pairings, slideIndex, totalSlides ?? slideIndex, slideKind)
                : pairings[(slideIndex - 1) % pairings.Count];
            var basePalette = ResolvePalette(brand, inferred);
            return basePalette with
            {
                Background = p.BackgroundHex,
                Text = p.TextHex,
                // three-colour-slide-combinations: when the pairing carries a third colour, it drives
                // the slide's emphasis colour. Null accent keeps the brand-derived accent untouched,
                // so two-colour clients render exactly as they did before.
                Accent = p.AccentTextHex ?? basePalette.Accent,
            };
        }

        return ResolvePalette(brand, inferred);
    }

    /// <summary>
    /// CSF-CG-03: picks an OBSERVED pairing for one slide.
    /// <list type="bullet">
    /// <item>A photo slide takes the client's photo pairing (text over their photos), falling back to
    /// the dominant flat pairing's colours when none was observed.</item>
    /// <item>Other slides take flat pairings allocated in proportion to their observed slide counts
    /// (largest remainder, dominant first). A client who used one combination on 10 slides and another
    /// on 1 gets the dominant one on every slide of a short carousel — never a 50/50 alternation.</item>
    /// </list>
    /// </summary>
    private static ColorPairing SelectObservedPairing(
        IReadOnlyList<ColorPairing> pairings,
        int slideIndex,
        int totalSlides,
        SlideBackgroundKind? slideKind)
    {
        var flat = pairings
            .Where(p => p.IsObserved && !IsPhotoPairing(p))
            .OrderByDescending(p => p.SlideCount)
            .ThenBy(p => p.Rank)
            .ToArray();

        if (slideKind == SlideBackgroundKind.Photo)
        {
            var photo = pairings
                .Where(p => p.IsObserved && IsPhotoPairing(p))
                .OrderByDescending(p => p.SlideCount)
                .FirstOrDefault();
            if (photo is not null)
            {
                return photo;
            }
        }

        if (flat.Length == 0)
        {
            return pairings.Where(p => p.IsObserved).OrderByDescending(p => p.SlideCount).First();
        }

        var allocation = AllocateByFrequency(flat.Select(p => p.SlideCount!.Value).ToArray(), Math.Max(1, totalSlides));
        var position = Math.Clamp(slideIndex - 1, 0, allocation.Count - 1);
        return flat[allocation[position]];
    }

    private static bool IsPhotoPairing(ColorPairing pairing) =>
        string.Equals(pairing.Kind, ColorPairingKinds.Photo, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Largest-remainder allocation of <paramref name="slots"/> slides across pairings weighted by
    /// <paramref name="counts"/> (already sorted, dominant first). Returns, for each slide position, the
    /// index of its pairing — dominant pairing's slides first, so the cover carries the client's most
    /// typical look.
    /// </summary>
    private static IReadOnlyList<int> AllocateByFrequency(IReadOnlyList<int> counts, int slots)
    {
        var total = counts.Sum();
        var quotas = counts.Select(c => (double)c * slots / total).ToArray();
        var assigned = quotas.Select(q => (int)Math.Floor(q)).ToArray();

        var remaining = slots - assigned.Sum();
        foreach (var index in quotas
                     .Select((q, i) => (Remainder: q - Math.Floor(q), Index: i))
                     .OrderByDescending(x => x.Remainder)
                     .ThenBy(x => x.Index)
                     .Take(remaining)
                     .Select(x => x.Index))
        {
            assigned[index]++;
        }

        var sequence = new List<int>(slots);
        for (var i = 0; i < assigned.Length; i++)
        {
            sequence.AddRange(Enumerable.Repeat(i, assigned[i]));
        }

        return sequence;
    }

    /// <summary>
    /// Smallest type we will ever emit. Below this, body copy stops being legible once the slide is
    /// scaled down to a feed thumbnail, so a bad measurement can never produce unreadable output.
    /// </summary>
    private const int MinTypeSize = 12;

    /// <summary>
    /// Largest fraction of canvas height a single line of type may claim. A headline past this is a
    /// measurement error, not a design choice.
    /// </summary>
    private const double MaxSizeRatio = 0.30;

    /// <summary>
    /// content-layout-and-image-fidelity WU-5 (CLIF-CG-07): copy-length-aware typography auto-fit.
    ///
    /// <para>
    /// This is a SEPARATE concern from WU-4's <c>MeasuredLayoutReflow</c>: reflow repositions
    /// SUBSEQUENT slots from a preceding slot's measured extent, but it cannot shrink a headline
    /// slot's OWN size when nothing sits below it to push, nor when the reflow clamp against the
    /// canvas bottom is reached (<c>MeasuredLayoutReflow</c>'s own clamp note: "that case is WU-5's
    /// auto-fit"). Below this length, a copy string is assumed typical and the resolved size is
    /// returned unchanged — this keeps every existing caller (which never passes copy) and every
    /// short-copy caller byte-identical (CLIF-CG-08).
    /// </para>
    /// </summary>
    private const int AutoFitCopyLengthFloor = 40;

    /// <summary>
    /// Copy length at which the maximum auto-fit reduction (down to <see cref="AutoFitMinRatio"/>)
    /// is reached. Reduction interpolates linearly between <see cref="AutoFitCopyLengthFloor"/> and
    /// this value.
    /// </summary>
    private const int AutoFitCopyLengthCeiling = 120;

    /// <summary>
    /// Auto-fit's stated floor: the resolved size is never reduced below this fraction of the
    /// unfit (rigid <see cref="SizeRatio"/>-derived) size, so a very long headline never becomes
    /// illegible.
    /// </summary>
    private const double AutoFitMinRatio = 0.70;

    /// <summary>
    /// Floor-bounded, reduction-only auto-fit. <paramref name="resolvedSize"/> is the rigid
    /// <c>SizeRatio</c>-derived size (never touched by this method for short copy);
    /// <paramref name="copyLength"/> is the headline's character count. Never increases the size
    /// (CLIF-CG-07-b); never reduces below <see cref="AutoFitMinRatio"/> of the unfit size
    /// (CLIF-CG-07-a).
    /// </summary>
    private static int ApplyCopyLengthAutoFit(int resolvedSize, int copyLength)
    {
        if (copyLength <= AutoFitCopyLengthFloor)
        {
            return resolvedSize;
        }

        var clampedLength = Math.Min(copyLength, AutoFitCopyLengthCeiling);
        var span = AutoFitCopyLengthCeiling - AutoFitCopyLengthFloor;
        var reductionFraction = (double)(clampedLength - AutoFitCopyLengthFloor) / span;
        var ratio = 1.0 - reductionFraction * (1.0 - AutoFitMinRatio);

        var fitted = (int)Math.Round(resolvedSize * ratio, MidpointRounding.AwayFromZero);
        var floor = (int)Math.Round(resolvedSize * AutoFitMinRatio, MidpointRounding.AwayFromZero);
        return Math.Max(floor, fitted);
    }

    /// <summary>
    /// Resolves typography from the client's OBSERVED per-role typography when the aesthetic profile
    /// carries it, falling back to the brand manual and then to defaults.
    ///
    /// <para>
    /// Sizes come from <see cref="AestheticTypography.SizeRatio"/> scaled by the canvas height, so the
    /// same profile drives a square carousel and a tall story without the copy changing apparent
    /// weight. The previous behaviour — one family for both roles at a hardcoded 28/16 px — survives
    /// unchanged whenever the profile has no typography, which keeps every existing client identical.
    /// </para>
    /// </summary>
    internal static CompositionTypography ResolveTypography(
        BrandSnapshot brand,
        ClientAestheticProfile? profile,
        int canvasHeight,
        InferenceAccumulator inferred,
        string? headlineCopy = null,
        int? canvasWidth = null)
    {
        var observed = profile?.Typography;
        if (observed is not { Count: > 0 })
        {
            return ResolveTypography(brand, inferred);
        }

        var heading = FindRole(observed, TypographyRoles.Heading);
        var body = FindRole(observed, TypographyRoles.Body);

        // A profile that names neither slot tells us nothing usable. Caption alone never activates
        // this branch — matches the "caption-only still early-outs" contract (D4).
        if (heading is null && body is null)
        {
            return ResolveTypography(brand, inferred);
        }

        inferred.Track("brand.Typography", false);

        var fallback = ResolveTypography(brand, inferred);

        var headingResolved = FontFamilyResolution.Resolve(heading?.FontFamily);
        var bodyResolved = FontFamilyResolution.Resolve(body?.FontFamily);

        // D4: caption role, with an overline alias and an accent legacy-synonym fallback — the
        // closest existing role when neither caption nor overline was named.
        var caption = FindRole(observed, TypographyRoles.Caption)
            ?? FindRole(observed, TypographyRoles.Overline)
            ?? FindRole(observed, TypographyRoles.Accent);
        var captionResolved = caption is null ? default : FontFamilyResolution.Resolve(caption.FontFamily);

        // WU-5 (CLIF-CG-07): floor-bounded, reduction-only auto-fit for unusually long copy. A null
        // or short headlineCopy is a no-op, so every pre-WU-5 caller (which never passes it) and
        // every short-copy caller stays byte-identical (CLIF-CG-08).
        var headingSize = SizeFor(heading, canvasHeight, fallback.HeadingSize);
        if (headlineCopy is not null)
        {
            headingSize = ApplyCopyLengthAutoFit(headingSize, headlineCopy.Length);
        }

        var headingFamily = heading is null ? fallback.HeadingFamily : headingResolved.Renderable;
        var bodyFamily = body is null ? fallback.BodyFamily : bodyResolved.Renderable;
        var captionFamily = caption is null ? null : captionResolved.Renderable;

        // client-style-fidelity (CSF-CG-04): every observed weight is clamped to what the resolved
        // family actually ships. Requesting 800 from a single-weight condensed face makes Chromium
        // synthesise a fake bold — the wide, heavy headline the client never uses.
        int? Clamp(string? family, int? weight) =>
            weight is null ? null : BundledFontCatalog.ClampWeight(family, weight.Value);

        return new CompositionTypography(
            HeadingFamily: headingFamily,
            HeadingSize: headingSize,
            BodyFamily: bodyFamily,
            BodySize: SizeFor(body, canvasHeight, fallback.BodySize))
        {
            HeadingWeight = Clamp(headingFamily, heading?.Weight) ?? 700,
            BodyWeight = Clamp(bodyFamily, body?.Weight) ?? 400,
            MeasuredBodyWeight = Clamp(bodyFamily, body?.Weight),
            BodyItalic = body?.Italic ?? false,
            RequestedHeadingFamily = heading is not null && !headingResolved.IsExact ? headingResolved.Requested : null,
            RequestedBodyFamily = body is not null && !bodyResolved.IsExact ? bodyResolved.Requested : null,
            CaptionFamily = captionFamily,
            CaptionSize = SizeForCaption(caption, canvasHeight),
            CaptionWeight = Clamp(captionFamily ?? bodyFamily, caption?.Weight) ?? 800,
            CaptionItalic = caption?.Italic ?? false,
            RequestedCaptionFamily = caption is not null && !captionResolved.IsExact ? captionResolved.Requested : null,
            // D1: nullable measured sizes for the markup builders' own hardcoded literals. Resolved the
            // same nullable way as CaptionSize (SizeForCaption), NOT via SizeFor's non-nullable
            // HeadingSize/BodySize above, which already default to 28/16px and would shrink a cold-start
            // headline if consumed directly by a builder.
            MeasuredHeadingSize = SizeForCaption(heading, canvasHeight),
            MeasuredBodySize = SizeForCaption(body, canvasHeight),
            // exact-client-typography follow-up: the client's REAL observed heading weight, so the
            // rendered markup can use it instead of each builder's own hardcoded font-weight literal.
            // Null (heading absent) keeps every builder's cold-start literal unchanged.
            MeasuredHeadingWeight = Clamp(headingFamily, heading?.Weight),
            MeasuredHeadingBlockWidth = BlockWidthFor(heading, canvasWidth),
        };
    }

    /// <summary>
    /// Narrowest and widest headline block we will honour, as a fraction of canvas width. Outside this
    /// range the measurement is an error: a 10 % block cannot hold a word, a 100 % block bleeds off
    /// the safe area.
    /// </summary>
    private const double MinBlockWidthRatio = 0.30;
    private const double MaxBlockWidthRatio = 0.90;

    /// <summary>
    /// Converts an observed block-width ratio into pixels (CSF-CG-05), clamped to
    /// <see cref="MinBlockWidthRatio"/>..<see cref="MaxBlockWidthRatio"/>. Null when unmeasured.
    /// </summary>
    private static int? BlockWidthFor(AestheticTypography? entry, int? canvasWidth)
    {
        if (entry?.BlockWidthRatio is not { } ratio || ratio <= 0 || ratio > 1 || canvasWidth is not > 0)
        {
            return null;
        }

        var clamped = Math.Clamp(ratio, MinBlockWidthRatio, MaxBlockWidthRatio);
        return (int)Math.Round(clamped * canvasWidth.Value, MidpointRounding.AwayFromZero);
    }

    private static AestheticTypography? FindRole(IReadOnlyList<AestheticTypography> entries, string role) =>
        entries.FirstOrDefault(e => string.Equals(e.Role?.Trim(), role, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Converts an observed size ratio into pixels for this canvas. An absent, non-positive, or
    /// implausibly large ratio means the extractor could not measure it, so the caller's existing
    /// default stands rather than a fabricated number.
    /// </summary>
    private static int SizeFor(AestheticTypography? entry, int canvasHeight, int fallbackSize)
    {
        if (entry is null || entry.SizeRatio <= 0 || entry.SizeRatio > MaxSizeRatio || canvasHeight <= 0)
        {
            return fallbackSize;
        }

        // AwayFromZero, not the Math.Round default: banker's rounding turns 40.5px into 40 and 41.5px
        // into 42, so two adjacent measurements round in opposite directions. Type sizes should just
        // round up at the midpoint.
        var pixels = (int)Math.Round(entry.SizeRatio * canvasHeight, MidpointRounding.AwayFromZero);
        return Math.Max(MinTypeSize, pixels);
    }

    /// <summary>
    /// Resolves typography from brand manual typography entries or returns defaults.
    /// </summary>
    internal static CompositionTypography ResolveTypography(BrandSnapshot brand, InferenceAccumulator inferred)
    {
        var hasTypography = brand.Brand?.Typography.Count > 0;
        inferred.Track("brand.Typography", !hasTypography);

        if (!hasTypography)
        {
            return Typography;
        }

        // Typography entry is a free-text font family name
        var fontFamily = brand.Brand!.Typography[0];
        return new CompositionTypography(fontFamily, 28, fontFamily, 16);
    }

    /// <summary>
    /// Converts an observed caption size ratio into pixels, or <c>null</c> when unmeasured/unusable —
    /// unlike <see cref="SizeFor"/> there is no hardcoded caption default to fall back to; a null keeps
    /// the caller's own hardcoded size (D4).
    /// </summary>
    private static int? SizeForCaption(AestheticTypography? entry, int canvasHeight)
    {
        if (entry is null || entry.SizeRatio <= 0 || entry.SizeRatio > MaxSizeRatio || canvasHeight <= 0)
        {
            return null;
        }

        var pixels = (int)Math.Round(entry.SizeRatio * canvasHeight, MidpointRounding.AwayFromZero);
        return Math.Max(MinTypeSize, pixels);
    }

    private static string Pick(IReadOnlyList<string> values, int index, string fallback) =>
        index < values.Count ? values[index] : fallback;
}
