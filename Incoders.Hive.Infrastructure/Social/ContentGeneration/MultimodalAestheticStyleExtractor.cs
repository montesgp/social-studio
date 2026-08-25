using System.Text.Json.Serialization;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// <see cref="IClientAestheticStyleExtractor"/> that learns a client's visual aesthetic and
/// conceptual art-direction method from their durably-hosted post images + captions using the org's
/// configured MULTIMODAL route (the optional <c>extract-aesthetic</c> / <c>ai-aesthetic</c> workflow
/// step). Phase 2 (CAG2-PA-03); extended in CACD with ConceptualMethod + ImageTypeRatio (v2).
/// </summary>
/// <remarks>
/// Reads each reference image's bytes from storage, base64-encodes them, and sends them as vision
/// input alongside a text instruction. Each image is tagged with the caption of its parent post when
/// available (CACD-PA-03; OQ-3). Palette is OPTIONAL output: an empty palette is a VALID result.
/// When no aesthetic route is configured, or no images resolve, returns
/// <see cref="AestheticExtraction.Empty"/> (the caller degrades to the Phase 1 text signal).
/// </remarks>
public sealed class MultimodalAestheticStyleExtractor : IClientAestheticStyleExtractor
{
    private const string CapabilityKey = "ai-aesthetic";
    // aesthetic-extractor/v3: adds brandMark {present,positionHint,colorHex} detection (FFS-PA-01).
    // aesthetic-extractor/v4: adds typicalSlideCount (median per-post slide count measured by the
    // model, replacing the BackgroundPattern-length proxy — measured-typical-slide-count follow-up).
    // Additive/backward-compatible: absent typicalSlideCount/brandMark keys on an older response still
    // map cleanly (null).
    // aesthetic-freshness-versioning: MUST stay equal to AestheticFreshness.CurrentSchemaVersion —
    // referencing the constant directly (both are compile-time consts) guarantees they never drift.
    private const string PromptVersion = AestheticFreshness.CurrentSchemaVersion;

    private readonly IAiCompletionGateway _gateway;
    private readonly IWorkflowConfigurationResolver _workflowResolver;
    private readonly IGeneratedAssetStorage _storage;
    private readonly ILogger<MultimodalAestheticStyleExtractor> _logger;

    public MultimodalAestheticStyleExtractor(
        IAiCompletionGateway gateway,
        IWorkflowConfigurationResolver workflowResolver,
        IGeneratedAssetStorage storage,
        ILogger<MultimodalAestheticStyleExtractor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(workflowResolver);
        ArgumentNullException.ThrowIfNull(storage);
        _gateway = gateway;
        _workflowResolver = workflowResolver;
        _storage = storage;
        _logger = logger ?? NullLogger<MultimodalAestheticStyleExtractor>.Instance;
    }

    public async Task<AestheticExtraction> ExtractAsync(
        Guid organizationId,
        IReadOnlyList<ProfilePostImageCaptionPair> referenceImages,
        CancellationToken ct)
    {
        if (referenceImages.Count == 0)
        {
            return AestheticExtraction.Empty;
        }

        // OQ-6: Resolve the OPTIONAL aesthetic route. The "ai-aesthetic" capability key MUST be
        // configured to a real vision-capable API model — e.g., openai / gpt-4o or
        // anthropic / claude-3-5-sonnet via BYOK API key.
        // The claude-p CLI provider is explicitly EXCLUDED: it is a backend-local CLI agent that
        // refuses JSON contracts and cannot perform structured multimodal vision extraction.
        // Configured model key: resolved per-org via IWorkflowConfigurationResolver.
        // No route configured → skip extraction (degrade to Phase 1 text-signal path).
        var route = await _workflowResolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                organizationId,
                "single-analyzer",
                "profile-analysis",
                "extract-aesthetic",
                "ai-aesthetic",
                null),
            ct);

        var candidate = route.IsSuccess
            ? route.Value.Where(c => c.IsEnabled).OrderBy(c => c.Priority).FirstOrDefault()
            : null;
        if (candidate is null)
        {
            _logger.LogInformation(
                "AestheticExtractor: no configured aesthetic (multimodal) route for org {OrganizationId} — skipping extraction.",
                organizationId);
            return AestheticExtraction.Empty;
        }

        // Load image bytes from durable storage and base64-encode for vision input.
        // Track loaded pairs so each image can be annotated with its post caption.
        var loadedPairs = new List<(AiImageInput Image, string? Caption)>(referenceImages.Count);
        foreach (var pair in referenceImages)
        {
            var opened = await _storage.OpenAsync(pair.Image.StoragePath, ct);
            if (opened.IsFailure)
            {
                _logger.LogWarning(
                    "AestheticExtractor: could not open reference image {Path} (org {OrganizationId}) — skipping it.",
                    pair.Image.StoragePath, organizationId);
                continue;
            }

            using var stream = opened.Value;
            using var buffer = new MemoryStream();
            await stream.Content.CopyToAsync(buffer, ct);
            loadedPairs.Add((new AiImageInput(stream.Mime, Convert.ToBase64String(buffer.ToArray())), pair.Caption));
        }

        if (loadedPairs.Count == 0)
        {
            return AestheticExtraction.Empty;
        }

        var images = loadedPairs.Select(lp => lp.Image).ToList();

        // Per-image caption annotations: each loaded image is tagged with the caption of its post
        // (CACD-PA-03; OQ-3). Caption = null → image-only marker (never throws).
        var captionAnnotations = loadedPairs
            .Select((lp, i) => lp.Caption is not null
                ? $"Image {i}: \"{lp.Caption}\""
                : $"Image {i}: (no caption)")
            .ToList();

        var captionBlock = "Post captions for each attached image (indexed left to right as attached):\n"
            + string.Join("\n", captionAnnotations);

        var instruction =
            "You are a brand-aesthetics analyst. The attached images are WHOLE recent carousels/posts of a " +
            "single Instagram client (multiple slides per post, in order). Study how the client composes a " +
            "post — clients often MIX a couple of creative/photographic slides with 2-3 flat colour/template " +
            "slides within the same carousel, and vary those templates across posts. Capture that COMBINATION " +
            "and recurring system, not just one image. Extract the client's VISUAL LANGUAGE so we can generate " +
            "NEW content that looks like THEY made it. Return: palette (role-ordered hex, ONLY if the client " +
            "clearly uses marked colours — otherwise return an EMPTY palette array, do not invent one), " +
            "styleDescriptors (composition, slide-mix pattern, layout system, mood, treatment), motifs " +
            "(recurring visual elements / template traits), and captionVoice (their copy tone). " +
            captionBlock + " " +
            // CACD-PA-01 (OQ-1): deterministic conceptual method elicitation.
            "Identify the client's ART-DIRECTION METHOD — how a MESSAGE becomes an IMAGE — and return " +
            "`method` inside a `conceptualMethod` object as EXACTLY ONE of: Unknown, MetaphoricalEditorial, " +
            "LiteralIllustration, SymbolicStillLife, DocumentaryPhotography, GraphicTypographic, Mixed. " +
            "Also return `rationale` (one sentence describing the client's message→visual relationship) " +
            "and `cues` (array of observable visual cues, e.g. \"mono-color saturated backdrop\"). " +
            // CACD-PA-03 (OQ-3): caption→visual relationship inference.
            "Each image is tagged above with the caption (message) of its post; use these captions to " +
            "infer HOW the client translates a message into a visual concept. " +
            // CACD-PA-02 (OQ-2): corpus-grounded image-type count.
            "Count, across ALL attached posts, how many slides are photographic (real photos or staged " +
            "photography), flat-color (solid background with template/text overlays), or decorator " +
            "(purely decorative / graphic dividers) and return `imageTypeCounts` " +
            "with fields `photo`, `flat`, and `decorator` (integer counts, not proportions). " +
            // measured-typical-slide-count: corpus-grounded slide-count measurement, replacing the
            // pattern-length proxy. MEDIAN (not mean) so a single outlier post cannot skew the count.
            "Also count how many slides EACH attached post has, and return `typicalSlideCount` as the " +
            "MEDIAN of those per-post slide counts (not the mean — it must resist outliers), as an " +
            "integer. If you cannot reliably tell how many slides a post has, do not guess — omit " +
            "`typicalSlideCount` (return null). " +
            // FFS-PA-01: recurring brand-mark detection (v3). Only report a mark that RECURS
            // consistently across the attached images — never invent one from a single occurrence.
            "Detect whether the SAME identifying mark (logo, emblem, watermark, or signature glyph) " +
            "recurs consistently across the attached images. Return `brandMark` with `present` " +
            "(boolean — true ONLY if a mark clearly recurs; otherwise false, do not guess), " +
            "`positionHint` (its approximate on-slide position, e.g. \"bottom-right\", " +
            "\"bottom-left\", \"bottom-center\", \"top-right\", \"top-left\", \"top-center\", " +
            "\"center\" — null when present is false or the position is unclear), and `colorHex` " +
            "(the mark's color as a hex string — typically the client's text color — null when " +
            "present is false or no distinct color is identifiable). " +
            // CAFF-PA-01 (exact-client-typography): three-role typography extraction. Each role is
            // independently optional — omit a role entirely when it is not distinguishable, never
            // invent one. This is separate from styleDescriptors: it is the CONCRETE type system
            // (family/weight/italic/size), not a mood description.
            "Also identify the TYPOGRAPHY the client actually uses on their slides, if any type is " +
            "visible and consistent enough to read off the pixels. Return `typography` with up to " +
            "three optional sub-objects: `heading` (the largest/boldest headline type), `body` (the " +
            "smaller supporting copy), and `caption` (small overline/caption/CTA-scale type, e.g. a " +
            "signature line or call-to-action). Each present sub-object has `family` (the font family " +
            "or closest-matching style name, e.g. \"Poppins\"), `weight` (CSS numeric weight 100-900), " +
            "`italic` (boolean), and `sizeRatio` (the type's height as a fraction of the slide's total " +
            "height, e.g. 0.08). Omit a sub-object entirely (do not fabricate placeholder values) when " +
            "that role is not reliably distinguishable in the attached images. " +
            // CAFF-PA-03 (client-aesthetic-fidelity-fixes, WU-3a): per-slide photo-transparency signal,
            // index-aligned with slideMixPattern. Boolean + qualitative descriptor ONLY — never a
            // numeric opacity/alpha value.
            "Also determine, for EACH slide in the SAME order as `slideMixPattern`, whether its " +
            "background photo is deliberately shown BLENDED/FADED (semi-transparent, composited over a " +
            "solid colour rather than a fully opaque photo). Return `slideBackgroundTransparency` as an " +
            "array, one entry per slide, aligned index-for-index with `slideMixPattern`. Each entry is " +
            "either an object with `blended` (boolean — true only when that slide clearly shows the " +
            "faded/blended treatment) and `descriptor` (a short qualitative phrase describing the " +
            "treatment, e.g. \"faded overlay\", \"soft blend\" — null when not applicable or not " +
            "describable), or `null` when you cannot reliably tell for that slide. NEVER return a " +
            "numeric opacity or alpha value for this signal — boolean + qualitative descriptor only.";

        var request = new AiJsonCompletionRequest(
            OrganizationId: organizationId,
            AnalysisId: null,
            CapabilityKey: CapabilityKey,
            PromptVersionKey: PromptVersion,
            InputJson: instruction,
            PreferredProviderKey: candidate.ProviderKey,
            PreferredModel: candidate.ModelId,
            Images: images);

        var result = await _gateway.CompleteJsonAsync<AestheticExtractionDto>(request, ct);
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "AestheticExtractor: extraction call failed for org {OrganizationId}: {ErrorCode}. Degrading to no aesthetic.",
                organizationId, result.Error.Code);
            return AestheticExtraction.Empty;
        }

        return Map(result.Value.Payload);
    }

    // Default background-candidate roles used when calling ColorPairingDerivation from the extractor.
    // The extractor has no access to the brand manual, so it considers all non-Neutral roles as candidates.
    private static readonly IReadOnlyList<ColorRole> DefaultBackgroundRoles =
        [ColorRole.Primary, ColorRole.Secondary, ColorRole.Accent];

    private static AestheticExtraction Map(AestheticExtractionDto dto)
    {
        var palette = (dto.Palette ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Hex))
            .Select((p, i) => new AestheticPaletteEntry(p.Hex!, p.Role, p.Rank > 0 ? p.Rank : i + 1))
            .ToArray();

        // client-aligned-carousel-generation: map slideMixPattern → BackgroundPattern (CAC-BSP-01-a)
        var backgroundPattern = MapSlideMixPattern(dto.SlideMixPattern);

        // client-aligned-carousel-generation: derive color pairings from extracted palette (CAC-BSP-03)
        // Uses all non-Neutral roles as background candidates since the brand manual is not available here.
        var colorPairings = ColorPairingDerivation.Derive(DefaultBackgroundRoles, palette);

        // CACD-PA-01 (OQ-1): map conceptualMethod DTO → ConceptualMethod value object.
        // Unrecognised method token → Unknown (deterministic; no nullable suppressor).
        // Null or empty rationale → ""; null cues → [].
        ConceptualMethod? conceptualMethod = null;
        if (dto.ConceptualMethod is { Method: { } methodStr })
        {
            var method = Enum.TryParse<ArtDirectionMethod>(methodStr, ignoreCase: false, out var parsed)
                ? parsed
                : ArtDirectionMethod.Unknown;
            conceptualMethod = new ConceptualMethod(
                method,
                string.IsNullOrWhiteSpace(dto.ConceptualMethod.Rationale) ? string.Empty : dto.ConceptualMethod.Rationale,
                dto.ConceptualMethod.Cues ?? []);
        }

        // CACD-PA-02 (OQ-2): normalize imageTypeCounts → ImageTypeRatio (proportions; SampleSize).
        // Null counts or zero total → null ratio (cold-start parity).
        ImageTypeRatio? imageTypeRatio = null;
        if (dto.ImageTypeCounts is { } counts)
        {
            var sampleSize = counts.Photo + counts.Flat + counts.Decorator;
            if (sampleSize > 0)
            {
                imageTypeRatio = new ImageTypeRatio(
                    Photo: (double)counts.Photo / sampleSize,
                    FlatColor: (double)counts.Flat / sampleSize,
                    Decorator: (double)counts.Decorator / sampleSize,
                    SampleSize: sampleSize);
            }
        }

        // Root-cause reconciliation: the ordered slideMixPattern (which slides look photographic) and the
        // imageTypeCounts (how many, statistically) are produced independently and can contradict — an
        // image-heavy client can still get an all-flat pattern that suppresses ALL image generation. The
        // counts are the reliable magnitude signal, so when the pattern under-represents photos, inject the
        // proportional number of photo slides. Only adds; never drops a pattern-derived photo slide.
        backgroundPattern = BackgroundPatternReconciler.Reconcile(backgroundPattern, imageTypeRatio);

        // CAFF-PA-03/04 (client-aesthetic-fidelity-fixes, WU-3a, D3): map the per-slide transparency
        // signal, then reconcile it against the (possibly just-reconciled) BackgroundPattern length so
        // the persisted array can never drift from the pattern it is index-aligned to. Defensive: an
        // absent `slideBackgroundTransparency` key, or a null model entry, never fabricates a value.
        IReadOnlyList<SlideBackgroundTransparency?>? backgroundTransparency = dto.SlideBackgroundTransparency is null
            ? null
            : dto.SlideBackgroundTransparency
                .Select(t => t is null ? null : new SlideBackgroundTransparency(t.Blended ?? false, t.Descriptor))
                .ToArray();
        backgroundTransparency = BackgroundTransparencyReconciler.Align(backgroundPattern, backgroundTransparency);

        // FFS-PA-01: map brandMark DTO → BrandMarkDetection VO. `Present: false` is a valid,
        // non-fabricated "no recurring mark" result — mapped whenever the model returns the
        // brandMark object at all. Absent brandMark key (older v2 responses) → null (cold-start
        // parity; FFS-CG-03-a).
        BrandMarkDetection? brandMark = dto.BrandMark is { } bm
            ? new BrandMarkDetection(bm.Present, bm.PositionHint, bm.ColorHex)
            : null;

        // CAFF-PA-01 (exact-client-typography, WU-2a): map the three optional typography roles.
        // Each role is independently additive-nullable — a role with no (or blank) family is simply
        // omitted from the list rather than fabricated; a client with zero roles yields Typography =
        // null (cold-start parity), matching every other defensive field in this Map().
        IReadOnlyList<AestheticTypography>? typography = null;
        if (dto.Typography is { } typo)
        {
            var roles = new List<AestheticTypography>(3);
            AddTypographyRole(roles, TypographyRoles.Heading, typo.Heading);
            AddTypographyRole(roles, TypographyRoles.Body, typo.Body);
            AddTypographyRole(roles, "caption", typo.Caption);
            typography = roles.Count > 0 ? roles : null;
        }

        return new AestheticExtraction(
            Palette: palette,
            StyleDescriptors: dto.StyleDescriptors ?? [],
            Motifs: dto.Motifs ?? [],
            CaptionVoice: string.IsNullOrWhiteSpace(dto.CaptionVoice) ? null : dto.CaptionVoice,
            BackgroundPattern: backgroundPattern,
            ColorPairings: colorPairings.Count > 0 ? colorPairings : null,
            ConceptualMethod: conceptualMethod,
            ImageTypeRatio: imageTypeRatio,
            BrandMark: brandMark,
            // aesthetic-freshness-versioning: stamp the extractor's OWN version onto the extraction
            // so persistence stores the ACTUAL version that produced it, not a hardcoded constant.
            SchemaVersion: PromptVersion,
            // measured-typical-slide-count: pass the model's measured median through unchanged.
            // Null when the model omitted it or could not tell (cold-start parity) — the caller
            // (AestheticProfilePersistence) falls back to BackgroundPattern's length in that case.
            TypicalSlideCount: dto.TypicalSlideCount,
            // CAFF-PA-01: three-role typography (heading/body/caption), mapped above.
            Typography: typography,
            // CAFF-PA-03/04: per-slide transparency signal, reconciled to BackgroundPattern's length.
            BackgroundTransparency: backgroundTransparency);
    }

    // CAFF-PA-01: defensive per-role mapping — a role with no (or blank) family is skipped rather
    // than added with a fabricated placeholder, matching the extractor's established convention for
    // every other optional AI-elicited field.
    private static void AddTypographyRole(List<AestheticTypography> roles, string role, TypographyRoleDto? dto)
    {
        if (dto is { Family: { } family } && !string.IsNullOrWhiteSpace(family))
        {
            roles.Add(new AestheticTypography(role, family, dto.Weight ?? 400, dto.Italic ?? false, dto.SizeRatio ?? 0));
        }
    }

    private static IReadOnlyList<SlideBackgroundKind>? MapSlideMixPattern(string[]? pattern)
    {
        if (pattern is null || pattern.Length == 0)
        {
            return null;
        }

        var result = new SlideBackgroundKind[pattern.Length];
        for (var i = 0; i < pattern.Length; i++)
        {
            result[i] = pattern[i].Trim().ToLowerInvariant() switch
            {
                "photo"       => SlideBackgroundKind.Photo,
                "flat"        => SlideBackgroundKind.FlatColor,
                "flatcolor"   => SlideBackgroundKind.FlatColor,
                "flat_color"  => SlideBackgroundKind.FlatColor,
                "decorator"   => SlideBackgroundKind.Decorator,
                _             => SlideBackgroundKind.FlatColor, // safe default for unknown values
            };
        }

        return result;
    }

    // -- AI response DTO (palette optional; slideMixPattern additive; CACD: conceptualMethod + imageTypeCounts additive) --
    private sealed record AestheticExtractionDto(
        [property: JsonPropertyName("palette")] PaletteEntryDto[]? Palette,
        [property: JsonPropertyName("styleDescriptors")] string[]? StyleDescriptors,
        [property: JsonPropertyName("motifs")] string[]? Motifs,
        [property: JsonPropertyName("captionVoice")] string? CaptionVoice,
        // client-aligned-carousel-generation: additive nullable field (CAC-BSP-01-a)
        [property: JsonPropertyName("slideMixPattern")] string[]? SlideMixPattern = null,
        // CACD-PA-01 (OQ-1): conceptual art-direction method (additive nullable; cold-start → null)
        [property: JsonPropertyName("conceptualMethod")] ConceptualMethodDto? ConceptualMethod = null,
        // CACD-PA-02 (OQ-2): aggregate image-type counts (additive nullable; cold-start → null)
        [property: JsonPropertyName("imageTypeCounts")] ImageTypeCountsDto? ImageTypeCounts = null,
        // FFS-PA-01: recurring brand mark (additive nullable; absent on pre-v3 responses → null)
        [property: JsonPropertyName("brandMark")] BrandMarkDto? BrandMark = null,
        // measured-typical-slide-count: median per-post slide count (additive nullable; absent on
        // pre-v4 responses, or when the model could not tell → null; cold-start parity)
        [property: JsonPropertyName("typicalSlideCount")] int? TypicalSlideCount = null,
        // CAFF-PA-01 (exact-client-typography): three optional typography roles (additive nullable;
        // absent on pre-WU-2a responses → null; cold-start parity)
        [property: JsonPropertyName("typography")] TypographyDto? Typography = null,
        // CAFF-PA-03 (client-aesthetic-fidelity-fixes, WU-3a): per-slide photo-transparency signal,
        // index-aligned with slideMixPattern (additive nullable; absent on pre-WU-3a responses → null;
        // cold-start parity). An entry itself may be null when the model could not tell for that slide.
        [property: JsonPropertyName("slideBackgroundTransparency")] SlideTransparencyDto?[]? SlideBackgroundTransparency = null);

    private sealed record PaletteEntryDto(
        [property: JsonPropertyName("hex")] string? Hex,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("rank")] int Rank);

    // CACD-PA-01: conceptual method sub-DTO
    private sealed record ConceptualMethodDto(
        [property: JsonPropertyName("method")] string? Method,
        [property: JsonPropertyName("rationale")] string? Rationale,
        [property: JsonPropertyName("cues")] string[]? Cues);

    // FFS-PA-01: brand-mark sub-DTO (present/positionHint/colorHex — v3)
    private sealed record BrandMarkDto(
        [property: JsonPropertyName("present")] bool Present,
        [property: JsonPropertyName("positionHint")] string? PositionHint,
        [property: JsonPropertyName("colorHex")] string? ColorHex);

    // CACD-PA-02: image-type counts sub-DTO (counts, not proportions — normalization is in Map)
    private sealed record ImageTypeCountsDto(
        [property: JsonPropertyName("photo")] int Photo,
        [property: JsonPropertyName("flat")] int Flat,
        [property: JsonPropertyName("decorator")] int Decorator);

    // CAFF-PA-01: three optional typography-role sub-DTO. Each role is independently nullable — a
    // missing/malformed role never fails deserialization (all-nullable properties).
    private sealed record TypographyDto(
        [property: JsonPropertyName("heading")] TypographyRoleDto? Heading = null,
        [property: JsonPropertyName("body")] TypographyRoleDto? Body = null,
        [property: JsonPropertyName("caption")] TypographyRoleDto? Caption = null);

    private sealed record TypographyRoleDto(
        [property: JsonPropertyName("family")] string? Family = null,
        [property: JsonPropertyName("weight")] int? Weight = null,
        [property: JsonPropertyName("italic")] bool? Italic = null,
        [property: JsonPropertyName("sizeRatio")] double? SizeRatio = null);

    // CAFF-PA-03: per-slide transparency sub-DTO — boolean + qualitative descriptor ONLY, never a
    // numeric opacity/alpha field (settled decision 2). Array elements are independently nullable so
    // the model can mark a specific slide as "cannot tell" without invalidating the whole array.
    private sealed record SlideTransparencyDto(
        [property: JsonPropertyName("blended")] bool? Blended = null,
        [property: JsonPropertyName("descriptor")] string? Descriptor = null);
}
