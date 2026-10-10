using System.Text.Json;
using System.Text.Json.Serialization;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;
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
    // aesthetic-extractor/v5 (WU-6a, CAGH-PA-01/02): brandMark gains markType ("text"/"icon"/"unknown")
    // and text (literal signature string, meaningful only for text marks). Additive/backward-compatible:
    // absent markType/text keys on an older response, or an unrecognised markType token, defensively
    // map to "unknown"/null rather than throwing (FFS-PA-02 convention).
    // aesthetic-extractor/v6 (WU-6b, CAGH-PA-03/04): brandMark gains sourceImageIndex (resolved
    // in-extractor to the post-skip loadedPairs StoragePath — D9) and boundingBox (normalised 0..1
    // crop region, validated — D10/CAGH-PA-04). Additive/backward-compatible: absent/malformed
    // sourceImageIndex or boundingBox keys defensively map to null rather than throwing.
    // aesthetic-extractor/v7 (WU-2, CAVS-PA-01/02/03): adds decorationMotifs {dividerRule,
    // compositionArchetype} detection. dividerRule maps through the closed DividerRuleStyle enum —
    // unrecognised/malformed tokens map to null, never throw (CAVS-PA-03-b). compositionArchetype is
    // an OPEN, normalised (trim+lowercase) token — an unrecognised token is preserved VERBATIM, never
    // collapsed to a placeholder (CAVS-PA-02-b, design D1). Additive/backward-compatible: absent
    // decorationMotifs key on an older response maps to null (cold-start parity).
    // aesthetic-extractor/v8 (WU-6, CAVS-PA-04): decorationMotifs gains a nested ornament
    // {sourceImageIndex, boundingBox} detection — a recurring decorative element (not the brand
    // mark). Mirrors the WU-6b brandMark sourceImageIndex/boundingBox extraction pattern verbatim:
    // sourceImageIndex resolves through the same post-skip loadedPairs StoragePath list (D9,
    // CAGH-PA-03 precedent) and boundingBox validates through the same
    // BrandMarkDetection.TryValidateBoundingBox/MinimumNormalizedArea guard (CAGH-PA-04 precedent).
    // Additive/backward-compatible: absent ornament key, or a malformed/out-of-range/below-minimum-
    // area box, defensively maps to null rather than throwing (CAVS-PA-04-b).
    // aesthetic-extractor/v9 (client-style-fidelity, CSF-PA-01..05): typography names a bundled catalog
    // family with ranked candidates, a located sample and the heading block width; the model reports
    // the text colour combinations it observes (with slide counts), which are then grounded in the
    // reference pixels; a divider must recur on a third of the text slides.
    // aesthetic-freshness-versioning: MUST stay equal to AestheticFreshness.CurrentSchemaVersion —
    // referencing the constant directly (both are compile-time consts) guarantees they never drift.
    private const string PromptVersion = AestheticFreshness.CurrentSchemaVersion;

    private readonly IAiCompletionGateway _gateway;
    private readonly IWorkflowConfigurationResolver _workflowResolver;
    private readonly IGeneratedAssetStorage _storage;
    private readonly ILogger<MultimodalAestheticStyleExtractor> _logger;
    private readonly IFontVisualMatcher? _fontMatcher;

    public MultimodalAestheticStyleExtractor(
        IAiCompletionGateway gateway,
        IWorkflowConfigurationResolver workflowResolver,
        IGeneratedAssetStorage storage,
        ILogger<MultimodalAestheticStyleExtractor>? logger = null,
        // CSF-PA-02: optional — without it the model's own catalog pick stands.
        IFontVisualMatcher? fontMatcher = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(workflowResolver);
        ArgumentNullException.ThrowIfNull(storage);
        _gateway = gateway;
        _workflowResolver = workflowResolver;
        _storage = storage;
        _logger = logger ?? NullLogger<MultimodalAestheticStyleExtractor>.Instance;
        _fontMatcher = fontMatcher;
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
        // Track loaded pairs so each image can be annotated with its post caption. D9 (WU-6b,
        // CAGH-PA-03): also track each pair's StoragePath so the model's Image {i} index — which is
        // the POST-SKIP loadedPairs index, since a failed OpenAsync above is `continue`d — can be
        // resolved to the archived storage path entirely inside this extractor.
        var loadedPairs = new List<(AiImageInput Image, string? Caption, string StoragePath, byte[] Bytes)>(referenceImages.Count);
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
            var bytes = buffer.ToArray();
            loadedPairs.Add((
                new AiImageInput(stream.Mime, Convert.ToBase64String(bytes)),
                pair.Caption,
                pair.Image.StoragePath,
                bytes));
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
            // CAGH-PA-01/02 (WU-6a, v5): classify the recurring mark and, when it is a text
            // signature, capture the literal string. Only meaningful when present is true.
            "When present is true, also classify the mark and return `markType` as EXACTLY ONE of " +
            "\"text\" (a written signature/wordmark, e.g. a handwritten or typeset name/handle) or " +
            "\"icon\" (a graphic logo/emblem/symbol with no legible words) — omit or return null when " +
            "present is false or you cannot tell which it is. When `markType` is \"text\", also " +
            "return `text` with the EXACT literal string as it appears (e.g. \"Dra. Lucia Fernandez\"); " +
            "never guess or paraphrase — omit/null `text` when the mark is an icon or the text is not " +
            "legible. " +
            // CAGH-PA-03/04 (WU-6b, v6): for an icon mark, locate its clearest occurrence so a crop
            // can be derived downstream. sourceImageIndex references the attached images by the SAME
            // "Image {i}" indexing used in the caption block above.
            "When `markType` is \"icon\", also return `sourceImageIndex` — the integer index (matching " +
            "the \"Image {i}\" numbering used for the captions above) of the ONE attached image where " +
            "the icon is most clearly visible — and `boundingBox`, a normalised crop region within that " +
            "image as `{\"x\":0..1,\"y\":0..1,\"width\":0..1,\"height\":0..1}` (origin top-left, " +
            "fractions of that image's width/height, tight around the icon). Omit/null both when the " +
            "mark is a text signature, `present` is false, or you cannot confidently localise the icon " +
            "— never guess a bounding box. " +
            // CAFF-PA-01 (exact-client-typography): three-role typography extraction. Each role is
            // independently optional — omit a role entirely when it is not distinguishable, never
            // invent one. This is separate from styleDescriptors: it is the CONCRETE type system
            // (family/weight/italic/size), not a mood description.
            "Also identify the TYPOGRAPHY the client actually uses on their slides, if any type is " +
            "visible and consistent enough to read off the pixels. Return `typography` with up to " +
            "three optional sub-objects: `heading` (the largest/boldest headline type), `body` (the " +
            "smaller supporting copy), and `caption` (small overline/caption/CTA-scale type, e.g. a " +
            "signature line or call-to-action). Each present sub-object has: `family` — EXACTLY one " +
            "name from this bundled font catalog, the one whose letterforms are closest to the " +
            "client's type: " + FontCatalogPromptList + ". Compare proportions before style: a tall, " +
            "very narrow headline face is a CONDENSED family, never a regular-width one, and a heavy " +
            "condensed face is not the same as a medium condensed one. `candidates` — up to 4 catalog " +
            "names ranked from most to least similar (the first equals `family`). `width` — " +
            "\"condensed\", \"normal\" or \"wide\". `weight` (CSS numeric weight 100-900, as it " +
            "looks), `italic` (boolean), and `sizeRatio` — the distance between two consecutive " +
            "baselines of that text divided by the slide's total height (for a single line, cap height " +
            "plus descender over slide height); MEASURE it on the pixels, do not estimate from " +
            "impression. `sampleText` — the EXACT words of the clearest occurrence of this role, " +
            "keeping accents and line order; `sourceImageIndex` — the integer index (matching the " +
            "\"Image {i}\" numbering above) of the image holding that occurrence; and `boundingBox` — " +
            "a normalised region tight around that text block as " +
            "`{\"x\":0..1,\"y\":0..1,\"width\":0..1,\"height\":0..1}` (origin top-left). For " +
            "`heading` also return `blockWidthRatio` — the width of the client's typical headline text " +
            "block divided by the slide width (e.g. 0.5 when headlines stack in the left half). Omit a " +
            "sub-object entirely (do not fabricate placeholder values) when that role is not reliably " +
            "distinguishable in the attached images. " +
            // CSF-PA-03 (client-style-fidelity): observed text colour combinations, with frequency.
            "Also return `textColorCombinations`: every combination of colours the client uses for " +
            "TEXT, as observed on the attached slides — one entry per distinct combination with " +
            "`backgroundKind` (\"flat\" when the text sits on a solid colour background, \"photo\" " +
            "when it sits over a photograph), `backgroundHex` (the solid background colour for flat; for " +
            "photo, the dominant tone directly behind the text), `textHex` (the main text colour), " +
            "`highlightHex` (the colour of emphasised words inside the same text when the client " +
            "highlights some words in a different colour; null otherwise), and `slideCount` (how many " +
            "attached slides use this combination). Only report combinations you actually SEE on the " +
            "slides; never derive one from the palette, and never report an accent colour as a " +
            "background unless whole slides really use it as their background. " +
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
            "numeric opacity or alpha value for this signal — boolean + qualitative descriptor only. " +
            // CAVS-PA-01/02/03 (client-aesthetic-visual-sophistication, WU-2): recurring decoration
            // signals — a divider-rule style and a composition-archetype classification. Both are
            // OPTIONAL; omit either when not reliably distinguishable, never invent one.
            "Also identify the client's recurring DECORATION signals, if any are consistent enough " +
            "across the attached images to report reliably. Return `decorationMotifs` with " +
            "`dividerRule` — EXACTLY ONE of \"line\" (a plain horizontal/vertical rule, no glyph), " +
            "\"line-with-diamond\" (a rule with a nested rotated-square glyph), or " +
            "\"line-with-dot\" (a rule with a nested dot glyph) — null when no recurring divider " +
            "rule is visible. Report a divider ONLY when the same rule appears on at least a third of " +
            "the slides that carry text; a line seen on one or two slides is not the client's style. Also return `compositionArchetype`, a short lowercase-hyphenated token " +
            "describing the client's recurring slide-layout system (e.g. \"left-aligned\", " +
            "\"centered\", \"lower-third\", or another token that best matches an archetype you " +
            "observe — do not force-fit an ill-matching one of those three), null when no consistent " +
            "layout archetype recurs across the attached images. " +
            // CAVS-PA-04 (client-aesthetic-visual-sophistication, WU-6): recurring decorative
            // ornament — a graphic element that is NOT the brand mark/logo.
            "Also identify a recurring decorative ornament (a graphic element that is NOT the brand " +
            "mark/logo — e.g. a repeated sticker, icon, or flourish that recurs across slides but " +
            "carries no identifying/signature meaning). Only report one that RECURS consistently — " +
            "never invent one from a single occurrence, and never confuse it with `brandMark`. " +
            "Nest it inside `decorationMotifs` as `ornament` with `sourceImageIndex` — the integer " +
            "index (matching the \"Image {i}\" " +
            "numbering used for the captions above) of the ONE attached image where the ornament is " +
            "most clearly visible — and `boundingBox`, a normalised crop region within that image as " +
            "`{\"x\":0..1,\"y\":0..1,\"width\":0..1,\"height\":0..1}` (origin top-left, fractions of " +
            "that image's width/height, tight around the ornament). Omit/null both when no recurring " +
            "ornament is visible or you cannot confidently localise it — never guess a bounding box.";

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

        // D9 (CAGH-PA-03): pass the post-skip StoragePath list so Map()/MapBrandMark() can resolve
        // the model's sourceImageIndex without depending on prompt ordering elsewhere.
        var storagePaths = loadedPairs.Select(lp => lp.StoragePath).ToArray();
        var sampler = SkiaColorCoverageSampler.FromImages(loadedPairs.Select(lp => lp.Bytes));
        var extraction = Map(result.Value.Payload, storagePaths, sampler.ImageCount > 0 ? sampler.MaxCoverage : null);

        // CSF-PA-02: confirm each role's family by comparing the client's real text against the
        // catalog candidates rendered side by side. Never fails the extraction.
        if (_fontMatcher is not null && extraction.Typography is { Count: > 0 } typography)
        {
            var requests = BuildFontMatchRequests(
                result.Value.Payload.Typography, loadedPairs.Select(lp => lp.Bytes).ToArray(), out var skipped);
            _logger.LogInformation(
                "AestheticExtractor: typography {Families} for org {OrganizationId}; visual font comparison for {Compared} role(s), skipped {Skipped}.",
                string.Join(", ", typography.Select(t => $"{t.Role}={t.FontFamily}")),
                organizationId,
                requests.Count,
                skipped.Count == 0 ? "none" : string.Join("; ", skipped));
            if (requests.Count > 0)
            {
                var chosen = await _fontMatcher.MatchAsync(
                    organizationId, candidate.ProviderKey, candidate.ModelId, requests, ct);
                if (chosen.Count > 0)
                {
                    _logger.LogInformation(
                        "AestheticExtractor: visual font comparison chose {Chosen} for org {OrganizationId}.",
                        string.Join(", ", chosen.Select(c => $"{c.Key}={c.Value}")),
                        organizationId);
                    extraction = extraction with
                    {
                        Typography = typography
                            .Select(t => chosen.TryGetValue(t.Role, out var family) ? t with { FontFamily = family } : t)
                            .ToArray(),
                    };
                }
            }
        }

        return extraction;
    }

    // Default background-candidate roles used when calling ColorPairingDerivation from the extractor.
    // The extractor has no access to the brand manual, so it considers all non-Neutral roles as candidates.
    private static readonly IReadOnlyList<ColorRole> DefaultBackgroundRoles =
        [ColorRole.Primary, ColorRole.Secondary, ColorRole.Accent];

    private static AestheticExtraction Map(
        AestheticExtractionDto dto,
        IReadOnlyList<string> storagePaths,
        Func<string, double>? coverage = null)
    {
        var palette = (dto.Palette ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Hex))
            .Select((p, i) => new AestheticPaletteEntry(p.Hex!, p.Role, p.Rank > 0 ? p.Rank : i + 1))
            .ToArray();

        // client-aligned-carousel-generation: map slideMixPattern → BackgroundPattern (CAC-BSP-01-a)
        var backgroundPattern = MapSlideMixPattern(dto.SlideMixPattern);

        // client-style-fidelity CSF-PA-03: combinations the model OBSERVED on the slides win. Without
        // them, fall back to deriving pairings from the palette's roles (CAC-BSP-03), which uses all
        // non-Neutral roles as background candidates since the brand manual is not available here.
        IReadOnlyList<ColorPairing> colorPairings = MapObservedCombinations(dto.TextColorCombinations);
        if (colorPairings.Count == 0)
        {
            colorPairings = ColorPairingDerivation.Derive(DefaultBackgroundRoles, palette);
        }

        // CSF-PA-04: a flat background must actually appear in the client's pixels. Skipped when no
        // reference image could be decoded — no evidence is not evidence of absence.
        if (coverage is not null)
        {
            colorPairings = ColorPairingEvidence.Ground(colorPairings, coverage);
        }

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
        // CAGH-PA-01/02 (WU-6a, v5): normalize markType defensively (never throws, unrecognised/
        // missing → "unknown"); text is meaningful — and kept — only when markType is "text".
        BrandMarkDetection? brandMark = dto.BrandMark is { } bm
            ? MapBrandMark(bm, storagePaths)
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

        // CAVS-PA-01/02/03 (client-aesthetic-visual-sophistication, WU-2): map decorationMotifs.
        // dividerRule defensively parses through CompositionArchetypes.TryParseDividerRule (closed
        // enum; unrecognised/malformed → null, never throws). compositionArchetype is normalised
        // (trim+lowercase) but NEVER collapsed — an unrecognised token is preserved verbatim (D1).
        // CAVS-PA-04 (WU-6): ornament mirrors the WU-6b brandMark sourceImageIndex/boundingBox
        // extraction pattern verbatim (MapOrnament, below). Absent decorationMotifs key, or a
        // decorationMotifs object with every signal unset (dividerRule/compositionArchetype/
        // ornament all null), maps to null (cold-start parity).
        DecorationMotifs? decorationMotifs = null;
        if (dto.DecorationMotifs is { } motifs)
        {
            var dividerRule = CompositionArchetypes.TryParseDividerRule(motifs.DividerRule);
            var compositionArchetype = CompositionArchetypes.Normalize(motifs.CompositionArchetype);
            var ornament = motifs.Ornament is { } ornamentDto ? MapOrnament(ornamentDto, storagePaths) : null;
            if (dividerRule is not null || compositionArchetype is not null || ornament is not null)
            {
                decorationMotifs = new DecorationMotifs(dividerRule, compositionArchetype, ornament);
            }
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
            BackgroundTransparency: backgroundTransparency,
            // CAVS-PA-01/02/03: recurring decoration signals, mapped above.
            DecorationMotifs: decorationMotifs);
    }

    /// <summary>
    /// The catalog the model must pick typography from (CSF-PA-01): every bundled family with its width
    /// and class, so "League Gothic (condensed display)" and "Oswald (condensed display)" are named,
    /// not guessed from memory.
    /// </summary>
    private static readonly string FontCatalogPromptList = string.Join(", ", BundledFontCatalog.Families.Select(f =>
        $"{f.Name} ({f.Width.ToString().ToLowerInvariant()} {f.Classification.ToString().ToLowerInvariant()})"));

    // CSF-PA-03: observed combinations → ColorPairing, most-used first. Entries without a valid
    // background/text hex are dropped; a missing slide count still counts as one observed slide.
    private static IReadOnlyList<ColorPairing> MapObservedCombinations(ColorCombinationDto?[]? combinations)
    {
        if (combinations is null)
        {
            return [];
        }

        return combinations
            .Where(c => c is not null
                        && SkiaColorCoverageSampler.TryParseHex(c.BackgroundHex, out _)
                        && SkiaColorCoverageSampler.TryParseHex(c.TextHex, out _))
            .Select(c => c!)
            .OrderByDescending(c => c.SlideCount ?? 1)
            .Select((c, index) => new ColorPairing(
                NormalizeHex(c.BackgroundHex!),
                NormalizeHex(c.TextHex!),
                index + 1,
                SkiaColorCoverageSampler.TryParseHex(c.HighlightHex, out _) ? NormalizeHex(c.HighlightHex!) : null,
                Math.Max(1, c.SlideCount ?? 1),
                string.Equals(c.BackgroundKind?.Trim(), ColorPairingKinds.Photo, StringComparison.OrdinalIgnoreCase)
                    ? ColorPairingKinds.Photo
                    : ColorPairingKinds.Flat))
            .ToArray();
    }

    // CSF-PA-02: a role is comparable when it names at least two catalog families and locates a
    // readable sample of the client's text.
    private static IReadOnlyList<FontMatchRequest> BuildFontMatchRequests(
        TypographyDto? typography,
        IReadOnlyList<byte[]> images,
        out List<string> skipped)
    {
        skipped = [];
        if (typography is null)
        {
            return [];
        }

        var requests = new List<FontMatchRequest>(3);
        foreach (var (role, dto) in new[]
                 {
                     (TypographyRoles.Heading, typography.Heading),
                     (TypographyRoles.Body, typography.Body),
                     (TypographyRoles.Caption, typography.Caption),
                 })
        {
            if (dto is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(dto.SampleText)
                || dto.SourceImageIndex is not { } index || index < 0 || index >= images.Count
                || dto.BoundingBox is not { } box)
            {
                skipped.Add($"{role}: no located sample");
                continue;
            }

            var validBox = BrandMarkDetection.TryValidateBoundingBox(box.X_, box.Y_, box.Width_, box.Height_);
            var candidates = new[] { dto.Family }
                .Concat(dto.Candidates ?? [])
                .Select(BundledFontCatalog.Find)
                .Where(f => f is not null)
                .Select(f => f!.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();
            if (validBox is null || candidates.Length < 2)
            {
                skipped.Add(validBox is null ? $"{role}: unusable sample box" : $"{role}: fewer than two catalog candidates");
                continue;
            }

            requests.Add(new FontMatchRequest(role, candidates, dto.SampleText, images[index], validBox));
        }

        return requests;
    }

    private static string NormalizeHex(string hex) => "#" + hex.Trim().TrimStart('#').ToUpperInvariant();

    // CAGH-PA-01/02 (WU-6a, v5): defensive brandMark mapping — markType is normalised through
    // BrandMarkDetection.NormalizeMarkType (never throws; missing/unrecognised → "unknown"); text is
    // kept only when the normalised markType is "text" and the raw value is not blank, so a
    // mismatched/model-hallucinated text on an icon mark is never surfaced (CAGH-PA-02-b).
    // CAGH-PA-03/04 (WU-6b, v6): SourceImagePath/BoundingBox are meaningful only for icon marks that
    // are actually Present — both null for text/unknown marks or absent marks, matching Text's guard.
    private static BrandMarkDetection MapBrandMark(BrandMarkDto bm, IReadOnlyList<string> storagePaths)
    {
        var markType = BrandMarkDetection.NormalizeMarkType(bm.MarkType);
        var text = markType == "text" && !string.IsNullOrWhiteSpace(bm.Text) ? bm.Text : null;

        string? sourceImagePath = null;
        BrandMarkBoundingBox? boundingBox = null;
        if (bm.Present && markType == "icon")
        {
            // CAGH-PA-03: resolve the model's post-skip loadedPairs index to a StoragePath. Any
            // unresolvable index (missing, negative, out of range) defensively maps to null.
            sourceImagePath = bm.SourceImageIndex is { } idx && idx >= 0 && idx < storagePaths.Count
                ? storagePaths[idx]
                : null;

            // CAGH-PA-04: validate/normalise the raw box; malformed/out-of-range/below-minimum-area
            // input maps to null, never throws.
            boundingBox = bm.BoundingBox is { } box
                ? BrandMarkDetection.TryValidateBoundingBox(box.X_, box.Y_, box.Width_, box.Height_)
                : null;
        }

        return new BrandMarkDetection(bm.Present, bm.PositionHint, bm.ColorHex, markType, text, sourceImagePath, boundingBox);
    }

    // CAVS-PA-04 (WU-6): defensive ornament mapping — mirrors MapBrandMark's icon-mark
    // sourceImageIndex/boundingBox extraction verbatim (same post-skip loadedPairs StoragePath
    // resolution, same TryValidateBoundingBox/MinimumNormalizedArea guard). Unlike brandMark, there
    // is no markType/present gate here: the ornament DTO only ever carries sourceImageIndex/
    // boundingBox, so any malformed/out-of-range/below-minimum-area input maps straight to null,
    // never throws (CAVS-PA-04-b).
    private static DecorativeOrnamentDetection? MapOrnament(OrnamentDto ornament, IReadOnlyList<string> storagePaths)
    {
        var sourceImagePath = ornament.SourceImageIndex is { } idx && idx >= 0 && idx < storagePaths.Count
            ? storagePaths[idx]
            : null;

        var boundingBox = ornament.BoundingBox is { } box
            ? BrandMarkDetection.TryValidateBoundingBox(box.X_, box.Y_, box.Width_, box.Height_)
            : null;

        return sourceImagePath is null && boundingBox is null
            ? null
            : new DecorativeOrnamentDetection(sourceImagePath, boundingBox);
    }

    // CAFF-PA-01: defensive per-role mapping — a role with no (or blank) family is skipped rather
    // than added with a fabricated placeholder, matching the extractor's established convention for
    // every other optional AI-elicited field.
    private static void AddTypographyRole(List<AestheticTypography> roles, string role, TypographyRoleDto? dto)
    {
        if (dto is { Family: { } family } && !string.IsNullOrWhiteSpace(family))
        {
            roles.Add(new AestheticTypography(
                role, family, dto.Weight ?? 400, dto.Italic ?? false, dto.SizeRatio ?? 0, dto.BlockWidthRatio));
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
        [property: JsonPropertyName("slideBackgroundTransparency")] SlideTransparencyDto?[]? SlideBackgroundTransparency = null,
        // CAVS-PA-01/02/03 (client-aesthetic-visual-sophistication, WU-2): recurring decoration
        // signals (additive nullable; absent on pre-v7 responses → null; cold-start parity).
        [property: JsonPropertyName("decorationMotifs")] DecorationMotifsDto? DecorationMotifs = null,
        // CSF-PA-03 (client-style-fidelity, v9): observed text colour combinations (additive nullable;
        // absent on pre-v9 responses → null, and the legacy derived pairings apply).
        [property: JsonPropertyName("textColorCombinations")] ColorCombinationDto?[]? TextColorCombinations = null);

    // CSF-PA-03: one observed text colour combination.
    private sealed record ColorCombinationDto(
        [property: JsonPropertyName("backgroundKind")] string? BackgroundKind = null,
        [property: JsonPropertyName("backgroundHex")] string? BackgroundHex = null,
        [property: JsonPropertyName("textHex")] string? TextHex = null,
        [property: JsonPropertyName("highlightHex")] string? HighlightHex = null,
        [property: JsonPropertyName("slideCount")] int? SlideCount = null);

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
    // CAGH-PA-01/02 (WU-6a, v5): markType/text additive nullable — absent on pre-v5 responses.
    // CAGH-PA-03/04 (WU-6b, v6): sourceImageIndex/boundingBox additive nullable — absent on pre-v6
    // responses; icon-only meaning is enforced at mapping time, not at the DTO shape.
    private sealed record BrandMarkDto(
        [property: JsonPropertyName("present")] bool Present,
        [property: JsonPropertyName("positionHint")] string? PositionHint,
        [property: JsonPropertyName("colorHex")] string? ColorHex,
        [property: JsonPropertyName("markType")] string? MarkType = null,
        [property: JsonPropertyName("text")] string? Text = null,
        [property: JsonPropertyName("sourceImageIndex")] int? SourceImageIndex = null,
        [property: JsonPropertyName("boundingBox")] BoundingBoxDto? BoundingBox = null);

    // CAGH-PA-04 (WU-6b, v6): raw bounding-box sub-DTO. Coordinates deserialize as JsonElement? —
    // NOT double? — so a non-numeric/malformed token (string, bool, array, NaN-as-string, etc.)
    // deserializes successfully as a non-Number-kind element instead of throwing a JsonException;
    // ReadDouble below is the single point that defensively converts to a nullable double.
    private sealed record BoundingBoxDto(
        [property: JsonPropertyName("x")] JsonElement? X = null,
        [property: JsonPropertyName("y")] JsonElement? Y = null,
        [property: JsonPropertyName("width")] JsonElement? Width = null,
        [property: JsonPropertyName("height")] JsonElement? Height = null)
    {
        public double? X_ => ReadDouble(X);
        public double? Y_ => ReadDouble(Y);
        public double? Width_ => ReadDouble(Width);
        public double? Height_ => ReadDouble(Height);

        private static double? ReadDouble(JsonElement? element) =>
            element is { ValueKind: JsonValueKind.Number } el && el.TryGetDouble(out var value)
                ? value
                : null;
    }

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
        [property: JsonPropertyName("sizeRatio")] double? SizeRatio = null,
        // CSF-PA-01 (v9): catalog candidates, width class, the clearest sample and the heading's block
        // width. All additive nullable — absent on pre-v9 responses.
        [property: JsonPropertyName("candidates")] string[]? Candidates = null,
        [property: JsonPropertyName("width")] string? Width = null,
        [property: JsonPropertyName("sampleText")] string? SampleText = null,
        [property: JsonPropertyName("sourceImageIndex")] int? SourceImageIndex = null,
        [property: JsonPropertyName("boundingBox")] BoundingBoxDto? BoundingBox = null,
        [property: JsonPropertyName("blockWidthRatio")] double? BlockWidthRatio = null);

    // CAFF-PA-03: per-slide transparency sub-DTO — boolean + qualitative descriptor ONLY, never a
    // numeric opacity/alpha field (settled decision 2). Array elements are independently nullable so
    // the model can mark a specific slide as "cannot tell" without invalidating the whole array.
    private sealed record SlideTransparencyDto(
        [property: JsonPropertyName("blended")] bool? Blended = null,
        [property: JsonPropertyName("descriptor")] string? Descriptor = null);

    // CAVS-PA-01/02/03: recurring decoration signals sub-DTO — dividerRule maps through the closed
    // DividerRuleStyle enum (defensive: unrecognised/malformed → null); compositionArchetype is an
    // OPEN raw token, normalised (never collapsed) at mapping time by CompositionArchetypes.Normalize.
    // CAVS-PA-04 (WU-6): ornament is additive nullable — absent on pre-v8 responses → null.
    private sealed record DecorationMotifsDto(
        [property: JsonPropertyName("dividerRule")] string? DividerRule = null,
        [property: JsonPropertyName("compositionArchetype")] string? CompositionArchetype = null,
        [property: JsonPropertyName("ornament")] OrnamentDto? Ornament = null);

    // CAVS-PA-04 (WU-6): recurring decorative ornament sub-DTO — mirrors BrandMarkDto's
    // sourceImageIndex/boundingBox shape (icon-mark meaning is n/a here; both fields are always
    // meaningful when present). BoundingBoxDto is reused verbatim (same defensive JsonElement? read).
    private sealed record OrnamentDto(
        [property: JsonPropertyName("sourceImageIndex")] int? SourceImageIndex = null,
        [property: JsonPropertyName("boundingBox")] BoundingBoxDto? BoundingBox = null);
}
