using System.Text.Json;
using System.Text.Json.Serialization;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Real <see cref="IVisualDesigner"/> implementation that calls the ORGANIZATION's configured
/// AI (provider + model) via <see cref="IAiCompletionGateway"/> to produce brand-bound HTML/CSS
/// markup. The model is NOT hardcoded — it follows the organization's workflow "generate-content"
/// route (priority-ordered) resolved through <see cref="IWorkflowConfigurationResolver"/>.
///
/// <para>
/// <see cref="IsActive"/> returns <c>true</c> — this is the live designer that replaces
/// <c>NoOpVisualDesigner</c> when B1b Infrastructure DI wiring is applied.
/// </para>
///
/// <para>
/// Design: Decision 5 (AI-as-Designer), Decision 9 (PromptVersionKey deterministic const
/// "visual-designer/v1"), Decision 10 (forward-compat layered markup: data-layer bg/content,
/// data-slot, data-max-lines).
/// </para>
///
/// <para>
/// Tier-2 multimodal (BackgroundSelection.References image input via vision blocks) is a
/// FOLLOW-UP. The current implementation uses text-only guidance for Tier-2 References.
/// See FOLLOW-UP note in tests and risks section of apply-progress.
/// </para>
/// </summary>
public sealed class AiVisualDesigner : IVisualDesigner
{
    private const string CapabilityKey = "content-generation";
    // v2: forbid the full-bleed gray scrim/overlay over real image backgrounds (legibility must come
    // from local text treatment), so image-background slides match the client's visual language.
    private const string PromptVersion = "visual-designer/v2";

    // Applied to image-background tiers only (forced-default / references): a real photographic/brand
    // template is the deliverable background, so a blanket scrim overloads the slide and breaks the
    // client's visual language. Legibility is solved locally on the text, never with a full-bleed veil.
    private const string NoScrimDirective =
        "DO NOT lay a full-bleed translucent scrim, tint, veil, gradient, or gray/black overlay box over " +
        "the image to boost legibility — it overloads the slide and does not match the client's visual " +
        "language. Keep the image clean and full-strength. Achieve text legibility LOCALLY instead: font " +
        "weight, color choice, letter-spacing, or a tight shadow/box behind the text only where the copy " +
        "sits — never covering the whole background.";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // Cap reference images sent to the designer per call (style reference, not many — bounds cost).
    private const int MaxDesignerReferenceImages = 4;

    private readonly IAiCompletionGateway _gateway;
    private readonly IWorkflowConfigurationResolver _workflowResolver;
    // Phase 2 (CAG2-CG-01): optional storage to load the client's durable reference images and send
    // them to the designer as VISION input, so it CREATES NEW backgrounds in the client's style
    // (we do NOT reuse the exact images). Null = no vision (text-first guidance only).
    private readonly IGeneratedAssetStorage? _assetStorage;

    public AiVisualDesigner(
        IAiCompletionGateway gateway,
        IWorkflowConfigurationResolver workflowResolver,
        IGeneratedAssetStorage? assetStorage = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(workflowResolver);
        _gateway = gateway;
        _workflowResolver = workflowResolver;
        _assetStorage = assetStorage;
    }

    /// <inheritdoc />
    /// <value>Always <c>true</c> — this is the active, live AI designer.</value>
    public bool IsActive => true;

    /// <inheritdoc />
    public async Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken,
        ClientAestheticDesignContext? aesthetic = null)
    {
        var inputJson = BuildInputJson(contentPayload, brandSnapshot, backgroundSelection, aesthetic);

        // Resolve the ORGANIZATION's configured AI from the workflow "generate-content" stage
        // (priority-ordered; priority-1 is the default). Same single agent the package orchestrator
        // uses — no per-format differentiation. NEVER hardcode the model: it follows the org config.
        var route = await _workflowResolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                organizationId,
                "single-analyzer",
                "content-analysis",
                "generate-content",
                "ai-generation",
                null),
            cancellationToken);
        if (route.IsFailure)
        {
            return Result.Failure<VisualDesignOutput>(route.Error);
        }

        var aiRoute = route.Value
            .Where(candidate => candidate.IsEnabled)
            .OrderBy(candidate => candidate.Priority)
            .FirstOrDefault();
        if (aiRoute is null)
        {
            return Result.Failure<VisualDesignOutput>(IntegrationErrors.AiRouteNotConfigured);
        }

        // Load the client's reference creatives as VISION input (CAG2-CG-01). The model SEES them and
        // creates NEW backgrounds in that visual line — it does NOT copy them. Best-effort + bounded.
        var visionImages = await LoadReferenceImagesAsync(aesthetic, cancellationToken);

        var request = new AiJsonCompletionRequest(
            OrganizationId: organizationId,
            AnalysisId: null,
            CapabilityKey: CapabilityKey,
            PromptVersionKey: PromptVersion,
            InputJson: inputJson,
            PreferredProviderKey: aiRoute.ProviderKey,
            PreferredModel: aiRoute.ModelId,
            Images: visionImages);

        var gatewayResult = await _gateway.CompleteJsonAsync<AiVisualDesignResponse>(request, cancellationToken);

        if (gatewayResult.IsFailure)
        {
            return Result.Failure<VisualDesignOutput>(gatewayResult.Error);
        }

        // Defensive extraction (VD-01 boundary): weaker models sometimes ignore the OUTPUT CONTRACT
        // and return a chatty preamble and/or a ```html code fence AROUND the composition. The
        // guardrail's FindComposition tolerantly validates the nested composition subtree, but the
        // RENDERER receives Markup unchanged — so any surrounding prose/fence would render verbatim
        // onto the slide. Strip the fence and surrounding prose so what renders == what is validated.
        var markup = ExtractCompositionMarkup(gatewayResult.Value.Payload.Markup);
        if (string.IsNullOrWhiteSpace(markup))
        {
            return Result.Failure<VisualDesignOutput>(
                Error.Failure("visual-designer.empty-markup", "AI designer returned empty markup."));
        }

        // IsValid = false — guardrails have not run yet (per VD-01 contract).
        var output = new VisualDesignOutput(
            Markup: markup,
            PromptVersionKey: PromptVersion,
            IsValid: false);

        return Result.Success(output);
    }

    /// <summary>
    /// Extracts the composition markup from a raw AI completion, defending against models that
    /// ignore the OUTPUT CONTRACT and wrap the composition in a chatty preamble and/or a Markdown
    /// code fence (e.g. <c>"Acá está la composición…\n```html\n&lt;div class=\"composition\"&gt;…"</c>).
    /// Unwraps the first fenced block when present, then trims any surrounding prose to the markup
    /// span (first <c>'&lt;'</c> to last <c>'&gt;'</c>). Returns an empty string when no markup is
    /// present at all, so the caller degrades the slide instead of rendering prose verbatim.
    /// Public+static for direct unit testing.
    /// </summary>
    public static string ExtractCompositionMarkup(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = raw.Trim();

        // 1) Unwrap the first Markdown fenced block, if any. Handles ```html / ```xml / bare ```.
        //    Everything before the opening fence (prose preamble) and after the closing fence
        //    (trailing prose) is discarded.
        var fenceStart = text.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var afterOpen = fenceStart + 3;
            var openLineEnd = text.IndexOf('\n', afterOpen);
            if (openLineEnd >= 0)
            {
                // The remainder of the opening-fence line is an optional language hint (e.g. "html").
                var contentStart = openLineEnd + 1;
                var fenceEnd = text.IndexOf("```", contentStart, StringComparison.Ordinal);
                text = (fenceEnd >= 0 ? text[contentStart..fenceEnd] : text[contentStart..]).Trim();
            }
        }

        // 2) Trim any remaining surrounding prose to the markup span (first '<' … last '>').
        //    When no angle brackets remain, there is no markup to render — return empty so the
        //    caller degrades the slide instead of printing the model's prose verbatim.
        var first = text.IndexOf('<');
        var last = text.LastIndexOf('>');
        if (first < 0 || last < first)
        {
            return string.Empty;
        }

        return text[first..(last + 1)].Trim();
    }

    /// <summary>
    /// D7: supplements — never replaces — the brand kit's <c>AllowedFonts</c> with the client's own
    /// resolved font classes (heading, body, caption — profile order), so a degenerate client set (0
    /// or 1 family) can never starve the designer. Client classes come first (preferred by the
    /// model), the brand kit is appended, and duplicates are removed case-insensitively. A null
    /// design context or an empty/unresolvable client set yields an array equal to today's
    /// <c>brand.Typography.ToArray()</c> exactly (cold-start parity).
    /// </summary>
    private static string[] BuildAllowedFonts(ClientAestheticDesignContext? aesthetic, BrandManualSnapshot brand)
    {
        var clientClasses = (aesthetic?.Typography ?? [])
            .Select(t => FontFamilyResolution.Resolve(t.FontFamily).Renderable)
            .Where(f => !string.IsNullOrWhiteSpace(f));

        return clientClasses
            .Concat(brand.Typography)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Builds the JSON input for the AI gateway.
    /// Embeds brand palette, typography, copy slots, canvas dimensions, and background selection.
    /// For Tier-1 ForcedDefault the exact URL is included so the prompt can reference it.
    /// For Tier-2 References, text-only descriptions are included (FOLLOW-UP: add vision blocks).
    /// For Tier-3 Creative, the designer generates a brand-bound background freely.
    /// </summary>
    private static string BuildInputJson(
        VisualCompositionSpec spec,
        BrandManualSnapshot brand,
        BackgroundSelection selection,
        ClientAestheticDesignContext? aesthetic = null)
    {
        var backgroundGuidance = selection switch
        {
            BackgroundSelection.ForcedDefault fd => new BackgroundGuidanceDto(
                Tier: "forced-default",
                BackgroundUrl: fd.Url,
                BackgroundName: fd.Name,
                ReferenceUrls: null,
                Description:
                    "MANDATORY: use the exact brand-manual background template as the full-bleed " +
                    $"background image. Do not invent, replace, recolor, crop creatively, or add generated " +
                    $"background art. Template URL: {fd.Url}. " +
                    NoScrimDirective),

            BackgroundSelection.References refs => new BackgroundGuidanceDto(
                Tier: "references",
                BackgroundUrl: null,
                BackgroundName: null,
                ReferenceUrls: refs.Entries.Select(e => e.Url).ToArray(),
                Description:
                    // Product rule: Incoders-Hive will hard-enforce the first applicable manual
                    // template as the actual rendered background. The URLs are still passed so the
                    // agent can align copy/hierarchy with the image it will be placed on.
                    "Manual background templates are available. Incoders-Hive will use the first applicable " +
                    "template as the actual full-bleed background; design only the text hierarchy/components " +
                    "that sit on top of it. " + NoScrimDirective + " Template URLs: "
                    + string.Join(", ", refs.Entries.Select(e => e.Url))),

            BackgroundSelection.Creative => new BackgroundGuidanceDto(
                Tier: "creative",
                BackgroundUrl: null,
                BackgroundName: null,
                ReferenceUrls: null,
                Description: "Generate a brand-appropriate background freely using the brand palette."),

            _ => new BackgroundGuidanceDto(
                Tier: "creative",
                BackgroundUrl: null,
                BackgroundName: null,
                ReferenceUrls: null,
                Description: "Generate a brand-appropriate background freely.")
        };

        // Palette resolution (CAG2-CG-01/02, CAG2-BSP-02; owner-approved rule change): for a
        // ClientAesthetic job (aesthetic is not null), the client's ANALYSIS is authoritative for
        // colour — the designer also receives up to MaxDesignerReferenceImages real client posts as
        // vision input (see LoadReferenceImagesAsync), so an analysis with no captured palette does NOT
        // fall back to the brand-manual palette: that would contradict the vision evidence and muddy the
        // prompt with dead-weight colours the model should ignore. We pass the analysis palette as-is —
        // it may legitimately be EMPTY, in which case the designer derives colour directly from the real
        // creatives it can see. A non-aesthetic job (aesthetic is null) keeps the brand-manual palette
        // unchanged — there is no client analysis to defer to.
        var brandPalette = brand.ColorPalette
            .Select(wc => new WeightedPaletteEntryDto(wc.Hex, wc.Role?.ToString(), wc.Rank))
            .ToArray();
        var palette = aesthetic is not null
            ? (aesthetic.Palette ?? [])
                .Select(p => new WeightedPaletteEntryDto(p.Hex, p.Role, p.Rank))
                .ToArray()
            : brandPalette;

        var input = new DesignerInputDto(
            FormatKey: spec.FormatKey,
            LayoutType: spec.LayoutType,
            Canvas: new CanvasDto(spec.Dimensions.Width, spec.Dimensions.Height),
            BrandPalette: palette,
            AllowedFonts: BuildAllowedFonts(aesthetic, brand),
            CopySlots: spec.CopySlots.ToDictionary(kv => kv.Key, kv => kv.Value),
            Background: backgroundGuidance,
            Aesthetic: BuildAestheticGuidance(aesthetic),
            SystemPrompt: BuildSystemPrompt(brand, aesthetic, spec.Typography, spec.Dimensions.Height));

        return JsonSerializer.Serialize(input, JsonOpts);
    }

    /// <summary>
    /// Loads up to <see cref="MaxDesignerReferenceImages"/> of the client's durable reference images
    /// from storage and returns them as vision inputs (base64). Returns null when no storage, no
    /// aesthetic context, or no references. Per-image best-effort: a failed read is skipped.
    /// </summary>
    private async Task<IReadOnlyList<AiImageInput>?> LoadReferenceImagesAsync(
        ClientAestheticDesignContext? aesthetic,
        CancellationToken cancellationToken)
    {
        if (_assetStorage is null || aesthetic is null || aesthetic.ReferenceImages.Count == 0)
        {
            return null;
        }

        var images = new List<AiImageInput>();
        foreach (var reference in aesthetic.ReferenceImages.Take(MaxDesignerReferenceImages))
        {
            try
            {
                var opened = await _assetStorage.OpenAsync(reference.StoragePath, cancellationToken);
                if (opened.IsFailure)
                {
                    continue;
                }

                using var stream = opened.Value;
                using var buffer = new MemoryStream();
                await stream.Content.CopyToAsync(buffer, cancellationToken);
                images.Add(new AiImageInput(stream.Mime, Convert.ToBase64String(buffer.ToArray())));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Best-effort: a missing/failed reference image must not fail the design.
            }
        }

        return images.Count > 0 ? images : null;
    }

    /// <summary>
    /// Builds the creatives-first aesthetic guidance block for a ClientAesthetic job (CAG2-CG-01).
    /// Returns null for non-ClientAesthetic jobs so the DTO field is omitted entirely.
    /// Reference images are passed as durable storage paths (text-first). True multimodal vision
    /// input (image bytes/blocks) is a FOLLOW-UP gated on the AI gateway gaining image support.
    /// </summary>
    private static AestheticGuidanceDto? BuildAestheticGuidance(ClientAestheticDesignContext? aesthetic)
    {
        if (aesthetic is null)
        {
            return null;
        }

        return new AestheticGuidanceDto(
            ReferenceImagePaths: aesthetic.ReferenceImages.Select(r => r.StoragePath).ToArray(),
            StyleDescriptors: aesthetic.StyleDescriptors.ToArray(),
            Motifs: aesthetic.Motifs.ToArray(),
            HasMarkedPalette: aesthetic.HasPalette,
            VariationSeed: aesthetic.VariationSeed);
    }

    private static string BuildSystemPrompt(
        BrandManualSnapshot brand,
        ClientAestheticDesignContext? aesthetic = null,
        // client-aesthetic-generation-hardening (D2): drives the headline size-guidance line below
        // from the client's OWN resolved size instead of a fixed range. Optional/default so no other
        // call site is forced to change.
        CompositionTypography? typography = null,
        int canvasHeight = 0)
    {
        // Build a prevalence-ordered palette description so the AI understands role weighting.
        // Primary dominates, Secondary supports, Accent is used sparingly, Neutral for backgrounds/text.
        var paletteEntries = brand.ColorPalette.Select(wc =>
            wc.Role.HasValue ? $"{wc.Role.Value} {wc.Hex}" : wc.Hex);
        var palette = string.Join(", ", paletteEntries);
        var fonts = string.Join(", ", brand.Typography);

        // Specific-over-general: the brand-manual notes are AUTHORITATIVE editorial directives. They can
        // require, place, or PROHIBIT a signature/handle or CTA, and override the defaults below.
        var notes = brand.Notes?.Trim();
        var directivesBlock = string.IsNullOrWhiteSpace(notes)
            ? string.Empty
            : "\n\nCLIENT DIRECTIVES (AUTHORITATIVE — honor literally; they OVERRIDE the rules below, including\n" +
              "any instruction to add, place, or PROHIBIT a signature/handle or call-to-action):\n" + notes;

        // CAG2-CG-01/02/03: ClientAesthetic — emulate the client's own visual language from their real
        // creatives. Lead with the client's style; treat palette as OPTIONAL; never invent a palette;
        // vary compositions within the client's coherent style.
        var aestheticBlock = string.Empty;
        if (aesthetic is not null)
        {
            var descriptors = aesthetic.StyleDescriptors.Count > 0
                ? string.Join("; ", aesthetic.StyleDescriptors)
                : "(infer from the reference creatives)";
            var motifs = aesthetic.Motifs.Count > 0 ? string.Join("; ", aesthetic.Motifs) : "(none specified)";
            var paletteDirective = aesthetic.HasPalette
                ? "Honour the role-ordered palette above as the client's colours."
                : "This client has NO marked colour palette — DERIVE colour directly from the client's " +
                  "reference creatives. DO NOT invent or impose a default/brand palette.";

            aestheticBlock =
                "\n\nCLIENT-AESTHETIC MODE (highest priority — make it look like the CLIENT made it):\n" +
                "A1. The goal is content INDISTINGUISHABLE from what this client already publishes. Emulate\n" +
                "    their visual language — composition, treatment, spacing, mood — not a generic template.\n" +
                $"A2. Client style descriptors: {descriptors}.\n" +
                $"A3. Recurring motifs to honour: {motifs}.\n" +
                $"A4. COLOUR: {paletteDirective}\n" +
                $"A5. VARIETY WITHIN COHERENCE: vary this composition (seed: {aesthetic.VariationSeed}) so a\n" +
                "    multi-asset package does not repeat one identical layout, while staying on the client's style.\n" +
                "A6. The ATTACHED images are the client's REAL recent posts. STUDY them and CREATE NEW\n" +
                "    backgrounds in the SAME visual line (palette, texture, composition, mood). DO NOT copy or\n" +
                "    reproduce them verbatim — produce fresh art that looks like the same author made it.\n" +
                "A7. Like this client, MIX background treatments across the package: some compositions over\n" +
                "    rich image-like/creative backgrounds, others over solid/color backgrounds — never all identical.";
        }

        // client-driven-decoration: rule 9 used to order "one or two soft geometric shapes/blobs" and
        // a "subtle shadow or glow" from EVERY client, which stamped translucent decorative circles
        // onto slides for clients whose real feed has none. When the corpus was MEASURED and shows the
        // client does not decorate, that order is replaced by an explicit prohibition — merely dropping
        // the rule is not an instruction, and the model decorates by default.
        var depthRule = aesthetic is not null && !aesthetic.AllowsDecoration
            ? "9. DEPTH: this client's own corpus contains NO decorative slides, so add NO decorative\n" +
              "   elements whatsoever - no geometric shapes, blobs, circles, glows, halos, accent bars,\n" +
              "   grain, or translucent overlays. Backgrounds are FLAT solid colour, or a photograph, and\n" +
              "   nothing else. Depth comes from typography, scale and composition alone.\n"
            : "9. DEPTH & MODERNITY: build the background in layers - gradients, one or two soft geometric\n" +
              "   shapes/blobs, an accent bar or shape behind the headline, a subtle shadow or glow, optional\n" +
              "   duotone or grain. Rounded corners. Tasteful and restrained - premium editorial, not clip-art.\n";

        // client-aesthetic-generation-hardening (D2): when the client's heading size was actually
        // measured, tell the designer the TRUE proportion instead of a fixed range that may not match
        // this client at all. Unmeasured/no canvas height → the pre-existing fixed literal, verbatim
        // (regression, CAGH-CG-02-b).
        var sizeGuidance = typography?.MeasuredHeadingSize is int measuredHeadingSize && canvasHeight > 0
            ? $"about {(int)Math.Round(measuredHeadingSize * 100.0 / canvasHeight, MidpointRounding.AwayFromZero)}% of canvas height"
            : "about 7-13% of canvas height";

        return
            "You are a SENIOR ART DIRECTOR designing premium social-media graphics (Instagram stories\n" +
            "and feed carousels). Produce ONE self-contained, brand-bound HTML composition styled with\n" +
            "INLINE CSS only.\n" +
            "\n" +
            "OUTPUT CONTRACT (non-negotiable - the rendering/export pipeline depends on it):\n" +
            "1. Output ONLY valid HTML. No markdown, no comments, no code fences, no explanation.\n" +
            "1b. ALL styling MUST be inline `style=\"...\"` attributes on the elements. Do NOT emit a\n" +
            "    <style> element, a <head>/<html>/<body> wrapper, <link>, <script>, or any CSS class\n" +
            "    rules — the validator allows ONLY these tags and REJECTS anything else: div, h1, h2, h3,\n" +
            "    h4, p, span, img, ul, ol, li, br, strong, em, b, i, u, small. A <style> block is the #1\n" +
            "    cause of rejection; put every rule inline instead.\n" +
            "2. Root element: <div class=\"composition\"> sized EXACTLY to the canvas (width/height in px).\n" +
            "3. Exactly TWO layers, in this order:\n" +
            "   - <div data-layer=\"background\"> = the full-bleed ART: background color/gradient/image AND\n" +
            "     EVERY decorative element (shapes, blobs, lines, accent bars, overlays, glow, grain).\n" +
            "   - <div data-layer=\"content\"> = ONLY text. Each text node is\n" +
            "     <tag class=\"slot\" data-slot=\"{slotName}\" data-max-lines=\"{n}\" style=\"...\">...</tag>.\n" +
            "   The exporter rasterizes the background layer and rebuilds the content text as EDITABLE\n" +
            "   boxes, so ALL decoration MUST live in background and ALL text MUST live in content slots,\n" +
            "   otherwise it is lost on export.\n" +
            "   NO INVENTED TEXT: the content layer must contain ONLY the provided copy slots. Do NOT add\n" +
            "   any extra headline, signature, @handle, username, or watermark — not even one you see in the\n" +
            "   attached reference images. Add a signature/handle ONLY when the client directives explicitly\n" +
            "   require it; then place it as a content slot (data-slot=\"signature\") with the directed text.\n" +
            "   NO INVENTED MARKS: do NOT draw, trace, approximate, or invent any logo, brand mark, emblem,\n" +
            "   symbol, icon, or signature glyph in EITHER layer — not even one you see in the attached\n" +
            "   reference images. A client's real identifying mark is supplied separately as an editable\n" +
            "   element when available; a hand-drawn guess never matches the real brand, so omit it entirely.\n" +
            "4. Allowed tags ONLY: div, h1, h2, h3, h4, p, span, img, ul, ol, li, br, strong, em, b, i, u, small.\n" +
            "5. Emit well-formed XHTML: self-close void elements (<img ... />, <br />), quote every\n" +
            "   attribute, escape literal & as &amp;, and use numeric entities (&#160;) - never named\n" +
            "   ones (&nbsp;, &mdash;). No JavaScript, no SVG, no @import, no Google Fonts, no remote fonts.\n" +
            "\n" +
            "DESIGN DIRECTION (ship work a senior designer would be proud of in 2026 - NOT a template):\n" +
            $"6. HIERARCHY: one dominant focal element. The headline is HUGE ({sizeGuidance})\n" +
            "   with tight line-height (~0.95-1.1). Sub-text and body are clearly smaller. Max 3 type sizes.\n" +
            "7. SPACE: let it breathe. Generous edge margins (>= 6-8% of width). Never crowd the edges;\n" +
            "   empty space is a deliberate design choice, not a gap to fill.\n" +
            "8. COMPOSITION: use the WHOLE canvas with intent - strong alignment to an invisible grid,\n" +
            "   anchor any CTA/signature that EXISTS in the copy slots toward the bottom, lead the eye top to\n" +
            "   bottom. Confident, purposeful placement (a bold off-center layout is welcome when it serves\n" +
            "   the hierarchy). Do not add a CTA or signature that is not in the copy slots or client directives.\n" +
            depthRule +
            $"10. COLOR: high contrast for legibility. The brand palette is ROLE-ORDERED by prevalence:\n" +
            $"    Primary = most dominant (use for hero backgrounds and key elements),\n" +
            $"    Secondary = supporting (complement the primary), Accent = sparingly (highlights only),\n" +
            $"    Neutral = backgrounds and text fields. Palette (rank-ordered): {palette}\n" +
            $"11. TYPOGRAPHY: use the brand family verbatim in every font-family declaration ({fonts}) — do\n" +
            "    NOT invent or substitute a different named font. If you cannot determine a brand family,\n" +
            "    use a CSS generic (e.g. sans-serif) and the pipeline will resolve it to the brand font;\n" +
            "    never a named system font (no \"Arial\", \"Helvetica\", or similar substitutes). Drive\n" +
            "    hierarchy with WEIGHT (heavy/black headline, lighter body). Uppercase + letter-spacing for\n" +
            "    small labels/kickers when it fits the design.\n" +
            "12. RESTRAINT: 2-3 colors, 2-3 type sizes, one accent. When in doubt, remove. Clean beats busy.\n" +
            "13. Inject brand colors as CSS vars (--brand-primary, --brand-secondary, --brand-accent) and use them." +
            aestheticBlock +
            directivesBlock;
    }

    // -----------------------------------------------------------------------
    // Private DTO types for JSON serialization only
    // -----------------------------------------------------------------------

    private sealed record DesignerInputDto(
        string FormatKey,
        string LayoutType,
        CanvasDto Canvas,
        WeightedPaletteEntryDto[] BrandPalette,
        string[] AllowedFonts,
        Dictionary<string, string> CopySlots,
        BackgroundGuidanceDto Background,
        AestheticGuidanceDto? Aesthetic,
        string SystemPrompt);

    private sealed record AestheticGuidanceDto(
        string[] ReferenceImagePaths,
        string[] StyleDescriptors,
        string[] Motifs,
        bool HasMarkedPalette,
        string VariationSeed);

    private sealed record WeightedPaletteEntryDto(string Hex, string? Role, int Rank);

    private sealed record CanvasDto(int Width, int Height);

    private sealed record BackgroundGuidanceDto(
        string Tier,
        string? BackgroundUrl,
        string? BackgroundName,
        string[]? ReferenceUrls,
        string Description);
}

/// <summary>
/// Response wrapper for the configured AI visual-design call.
/// The AI gateway deserializes the JSON payload into this shape.
/// </summary>
public sealed record AiVisualDesignResponse(
    [property: JsonPropertyName("markup")] string Markup);
