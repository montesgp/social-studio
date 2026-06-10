using System.Text.Json;
using System.Text.Json.Serialization;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Real <see cref="IVisualDesigner"/> implementation that calls the ORGANIZATION's configured
/// default AI (provider + model) via <see cref="IAiCompletionGateway"/> to produce brand-bound
/// HTML/CSS markup. The model is NOT hardcoded — it follows Settings → Integrations → default AI
/// (the request passes null provider/model so the gateway resolves the org default).
///
/// <para>
/// NOTE: the type name is historical ("Opus"); it is model-agnostic now. Rename to
/// <c>AiVisualDesigner</c> is a safe follow-up.
/// </para>
///
/// <para>
/// <see cref="IsActive"/> returns <c>true</c> — this is the live designer that replaces
/// <c>NoOpVisualDesigner</c> when B1b Infrastructure DI wiring is applied.
/// </para>
///
/// <para>
/// Design: Decision 5 (Opus-as-Designer), Decision 9 (PromptVersionKey deterministic const
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
public sealed class OpusVisualDesigner : IVisualDesigner
{
    private const string CapabilityKey = "content-generation";
    private const string PromptVersion = "visual-designer/v1";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IAiCompletionGateway _gateway;
    private readonly IDefaultAiSelector _aiSelector;

    public OpusVisualDesigner(IAiCompletionGateway gateway, IDefaultAiSelector aiSelector)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(aiSelector);
        _gateway = gateway;
        _aiSelector = aiSelector;
    }

    /// <inheritdoc />
    /// <value>Always <c>true</c> — this is the active, live Opus designer.</value>
    public bool IsActive => true;

    /// <inheritdoc />
    public async Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken)
    {
        var inputJson = BuildInputJson(contentPayload, brandSnapshot, backgroundSelection);

        // Resolve the ORGANIZATION's configured AI exactly like the copy/analysis path does, via
        // IDefaultAiSelector. This is lenient (falls back to a default provider when no explicit
        // default-AI flag is set), so it works whether or not the org marked a default AI — unlike
        // passing null provider, which forces the gateway's strict default-AI lookup and 500s when
        // none is flagged. NEVER hardcode the model: it follows whatever the org selected.
        var selection = await _aiSelector.SelectAsync(organizationId, cancellationToken);
        if (selection.IsFailure)
        {
            return Result.Failure<VisualDesignOutput>(selection.Error);
        }

        var request = new AiJsonCompletionRequest(
            OrganizationId: organizationId,
            AnalysisId: null,
            CapabilityKey: CapabilityKey,
            PromptVersionKey: PromptVersion,
            InputJson: inputJson,
            PreferredProviderKey: selection.Value.ProviderKey,
            PreferredModel: selection.Value.Model);

        var gatewayResult = await _gateway.CompleteJsonAsync<OpusHtmlResponse>(request, cancellationToken);

        if (gatewayResult.IsFailure)
        {
            return Result.Failure<VisualDesignOutput>(gatewayResult.Error);
        }

        var markup = gatewayResult.Value.Payload.Markup;
        if (string.IsNullOrWhiteSpace(markup))
        {
            return Result.Failure<VisualDesignOutput>(
                Error.Failure("visual-designer.empty-markup", "Opus returned empty markup."));
        }

        // IsValid = false — guardrails have not run yet (per VD-01 contract).
        var output = new VisualDesignOutput(
            Markup: markup,
            PromptVersionKey: PromptVersion,
            IsValid: false);

        return Result.Success(output);
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
        BackgroundSelection selection)
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
                    $"background art. Template URL: {fd.Url}"),

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
                    "that sit on top of it. Template URLs: "
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

        var input = new DesignerInputDto(
            FormatKey: spec.FormatKey,
            LayoutType: spec.LayoutType,
            Canvas: new CanvasDto(spec.Dimensions.Width, spec.Dimensions.Height),
            BrandPalette: brand.ColorPalette.ToArray(),
            AllowedFonts: brand.Typography.ToArray(),
            CopySlots: spec.CopySlots.ToDictionary(kv => kv.Key, kv => kv.Value),
            Background: backgroundGuidance,
            SystemPrompt: BuildSystemPrompt(brand));

        return JsonSerializer.Serialize(input, JsonOpts);
    }

    private static string BuildSystemPrompt(BrandManualSnapshot brand)
    {
        var palette = string.Join(", ", brand.ColorPalette);
        var fonts = string.Join(", ", brand.Typography);

        return
            "You are a SENIOR ART DIRECTOR designing premium social-media graphics (Instagram stories\n" +
            "and feed carousels). Produce ONE self-contained, brand-bound HTML/CSS composition.\n" +
            "\n" +
            "OUTPUT CONTRACT (non-negotiable - the rendering/export pipeline depends on it):\n" +
            "1. Output ONLY valid HTML. No markdown, no comments, no code fences, no explanation.\n" +
            "2. Root element: <div class=\"composition\"> sized EXACTLY to the canvas (width/height in px).\n" +
            "3. Exactly TWO layers, in this order:\n" +
            "   - <div data-layer=\"background\"> = the full-bleed ART: background color/gradient/image AND\n" +
            "     EVERY decorative element (shapes, blobs, lines, accent bars, overlays, glow, grain).\n" +
            "   - <div data-layer=\"content\"> = ONLY text. Each text node is\n" +
            "     <tag class=\"slot\" data-slot=\"{slotName}\" data-max-lines=\"{n}\" style=\"...\">...</tag>.\n" +
            "   The exporter rasterizes the background layer and rebuilds the content text as EDITABLE\n" +
            "   boxes, so ALL decoration MUST live in background and ALL text MUST live in content slots,\n" +
            "   otherwise it is lost on export.\n" +
            "4. Allowed tags ONLY: div, h1, h2, h3, h4, p, span, img, ul, ol, li, br, strong, em, b, i, u, small.\n" +
            "5. Emit well-formed XHTML: self-close void elements (<img ... />, <br />), quote every\n" +
            "   attribute, escape literal & as &amp;, and use numeric entities (&#160;) - never named\n" +
            "   ones (&nbsp;, &mdash;). No JavaScript, no SVG, no @import, no Google Fonts, no remote fonts.\n" +
            "\n" +
            "DESIGN DIRECTION (ship work a senior designer would be proud of in 2026 - NOT a template):\n" +
            "6. HIERARCHY: one dominant focal element. The headline is HUGE (about 7-13% of canvas height)\n" +
            "   with tight line-height (~0.95-1.1). Sub-text and body are clearly smaller. Max 3 type sizes.\n" +
            "7. SPACE: let it breathe. Generous edge margins (>= 6-8% of width). Never crowd the edges;\n" +
            "   empty space is a deliberate design choice, not a gap to fill.\n" +
            "8. COMPOSITION: use the WHOLE canvas with intent - strong alignment to an invisible grid,\n" +
            "   anchor CTA and signature toward the bottom, lead the eye top to bottom. Confident,\n" +
            "   purposeful placement (a bold off-center layout is welcome when it serves the hierarchy).\n" +
            "9. DEPTH & MODERNITY: build the background in layers - gradients, one or two soft geometric\n" +
            "   shapes/blobs, an accent bar or shape behind the headline, a subtle shadow or glow, optional\n" +
            "   duotone or grain. Rounded corners. Tasteful and restrained - premium editorial, not clip-art.\n" +
            $"10. COLOR: high contrast for legibility. Use the brand palette boldly and pick ONE accent for\n" +
            $"    emphasis. Neutrals (white/black/grays) are fine for text and background. Brand palette: {palette}\n" +
            $"11. TYPOGRAPHY: prefer the brand fonts ({fonts}); a clean system fallback is acceptable. Drive\n" +
            "    hierarchy with WEIGHT (heavy/black headline, lighter body). Uppercase + letter-spacing for\n" +
            "    small labels/kickers when it fits the design.\n" +
            "12. RESTRAINT: 2-3 colors, 2-3 type sizes, one accent. When in doubt, remove. Clean beats busy.\n" +
            "13. Inject brand colors as CSS vars (--brand-primary, --brand-secondary, --brand-accent) and use them.";
    }

    // -----------------------------------------------------------------------
    // Private DTO types for JSON serialization only
    // -----------------------------------------------------------------------

    private sealed record DesignerInputDto(
        string FormatKey,
        string LayoutType,
        CanvasDto Canvas,
        string[] BrandPalette,
        string[] AllowedFonts,
        Dictionary<string, string> CopySlots,
        BackgroundGuidanceDto Background,
        string SystemPrompt);

    private sealed record CanvasDto(int Width, int Height);

    private sealed record BackgroundGuidanceDto(
        string Tier,
        string? BackgroundUrl,
        string? BackgroundName,
        string[]? ReferenceUrls,
        string Description);
}

/// <summary>
/// Response wrapper for the Opus HTML generation call.
/// The AI gateway deserializes the JSON payload into this shape.
/// </summary>
public sealed record OpusHtmlResponse(
    [property: JsonPropertyName("markup")] string Markup);
