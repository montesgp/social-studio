using System.Text.Json;
using System.Text.Json.Serialization;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Real <see cref="IVisualDesigner"/> implementation that calls Claude Opus via
/// <see cref="IAiCompletionGateway"/> to produce brand-bound HTML/CSS markup.
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

    public OpusVisualDesigner(IAiCompletionGateway gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        _gateway = gateway;
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

        var request = new AiJsonCompletionRequest(
            OrganizationId: organizationId,
            AnalysisId: null,
            CapabilityKey: CapabilityKey,
            PromptVersionKey: PromptVersion,
            InputJson: inputJson,
            PreferredProviderKey: "claude",
            PreferredModel: "claude-opus-4-5");

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
                Description: $"Use the exact background image at URL: {fd.Url}"),

            BackgroundSelection.References refs => new BackgroundGuidanceDto(
                Tier: "references",
                BackgroundUrl: null,
                BackgroundName: null,
                ReferenceUrls: refs.Entries.Select(e => e.Url).ToArray(),
                Description:
                    // FOLLOW-UP: when IAiCompletionGateway supports multimodal vision blocks,
                    // pass the reference images as image inputs rather than URLs in text.
                    "Use these reference images for visual style guidance (text-only fallback — vision is a follow-up): "
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
            "You are a professional visual designer for social media content.\n" +
            "Produce self-contained, brand-bound HTML/CSS markup for a single composition.\n" +
            "\n" +
            "RULES (non-negotiable):\n" +
            "1. Output ONLY valid HTML — no markdown, no explanation, no code fences.\n" +
            "2. Root element: <div class=\"composition\"> with width/height set to the canvas size in px.\n" +
            "3. Two required child layers:\n" +
            "   - <div data-layer=\"background\"> — background color, gradient, or image\n" +
            "   - <div data-layer=\"content\"> — all text and foreground elements\n" +
            "4. Every text node MUST be: <element class=\"slot\" data-slot=\"{slotName}\" data-max-lines=\"{n}\">\n" +
            $"5. Colors: PREFER the brand palette for all primary, accent and decorative elements: {palette}\n" +
            "   Neutral colors — white (#FFFFFF), black (#000000) and grays — ARE allowed for text,\n" +
            "   backgrounds and contrast when the palette has no suitable neutral. Do NOT introduce\n" +
            "   other off-brand hues; lean on the brand colors as much as the layout allows.\n" +
            $"6. Fonts: PREFER the font families from the brand typography list: {fonts}\n" +
            "   If the brand list has no suitable family for a role, a neutral system fallback is\n" +
            "   acceptable. Avoid decorative off-brand fonts.\n" +
            "7. No external @import, no Google Fonts, no remote URLs for fonts.\n" +
            "   Fonts are container-installed — reference by family name only.\n" +
            "8. No JavaScript. No SVG. Allowed tags: div, h1, h2, p, span, img, ul, li.\n" +
            "9. Inject brand colors as CSS vars: --brand-primary, --brand-secondary, --brand-accent.";
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
