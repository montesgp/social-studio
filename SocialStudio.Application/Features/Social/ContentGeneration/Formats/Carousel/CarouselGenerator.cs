using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Observability;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.Logging;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Formats.Carousel;

/// <summary>
/// Format generator for Instagram/LinkedIn carousel posts.
/// Produces <see cref="CarouselContent"/> from the analysis + brand snapshot.
/// </summary>
public sealed class CarouselGenerator : FormatContentGeneratorBase<CarouselContent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CarouselGenerator(
        IAiCompletionGateway aiGateway,
        ProviderCallContext providerCallContext,
        ILogger<CarouselGenerator>? logger = null)
        : base(aiGateway, providerCallContext, logger) { }

    private const string CtaTextShape =
        "string|null - ONLY when explicitly requested by the user or defined by a brand-manual directive; null otherwise — never inferred or invented";
    private const string CtaLinkShape = "string|null - optional URL for the CTA";
    private const string PublicationDescriptionShape =
        "string - ready-to-publish caption/post copy for this carousel; ALWAYS provide it, never null";
    private const string HashtagsShape =
        "array of string - relevant hashtags for the post caption (without duplicates); [] if none apply";

    /// <inheritdoc />
    public override string FormatKey => "carousel";

    /// <inheritdoc />
    public override string PromptVersion => ContentGenerationPromptVersions.Carousel;

    /// <summary>
    /// Resolves the effective slide count for a carousel.
    /// OQ-3 priority: explicit user-requested count wins; else profile TypicalSlideCount clamped to [3..10];
    /// else null (caller falls back to current default text hint).
    /// Refs: CAC-CG-01; design §0 OQ-3; tasks 2.4.
    /// </summary>
    /// <param name="userRequested">Explicit user-requested count, if any.</param>
    /// <param name="typicalSlideCount">Profile-captured typical slide count, if any.</param>
    /// <returns>Resolved effective count, or null when neither source provides a value.</returns>
    public static int? ResolveEffectiveSlideCount(int? userRequested, int? typicalSlideCount)
    {
        if (userRequested.HasValue)
            return userRequested.Value;

        if (typicalSlideCount.HasValue)
            return Math.Clamp(typicalSlideCount.Value, 3, 10);

        return null;
    }

    /// <inheritdoc />
    protected override string BuildPromptJson(GenerationContext ctx)
    {
        // WU-4 (CAG-CG-08): aesthetic guidance appended AFTER ConcisionConstraint (additive, advisory).
        // Only CarouselGenerator emits the avg-slides hint (carousel-only advisory per design §6).
        // Non-CaseC jobs: ctx.AestheticProfile is null → AestheticGuidance returns "" → no change.
        var aestheticGuidance = GenerationPromptContext.AestheticGuidance(ctx.AestheticProfile);
        var aestheticSuffix = aestheticGuidance.Length > 0 ? " " + aestheticGuidance : string.Empty;

        // WU-2 (CAC-CG-01 / OQ-3): replace hardcoded ~3-5 with the resolved effective slide count.
        // userRequested wins; else profile TypicalSlideCount clamped [3..10]; else fallback hint.
        var effectiveSlideCount = ResolveEffectiveSlideCount(
            ctx.RequestedSlideCount,
            ctx.AestheticProfile?.TypicalSlideCount);
        var slideScopeHint = effectiveSlideCount.HasValue
            ? $"~{effectiveSlideCount} slides"
            : "~3-5 slides";

        // client-style-fidelity CSF-CG-06: only a client OBSERVED highlighting words gets highlights.
        var highlightsWords = ClientHighlightsWords(ctx.AestheticProfile);
        var highlightRule = highlightsWords
            ? "HIGHLIGHT RULE: this client emphasises ONE key phrase of each headline in a contrasting colour. " +
              "For coverTitle and every slide title, return in coverTitleHighlight / titleHighlight the exact " +
              "1-3 consecutive words to emphasise, copied verbatim from that title (same spelling, accents and " +
              "case) - the words carrying the message, e.g. \"tus clientes\" in \"¿Sabés qué toman tus " +
              "clientes en tu local?\". "
            : string.Empty;

        var input = new
        {
            instruction = "Generate a branded carousel post with a cover slide and content slides. " +
                          GenerationPromptContext.ClientDirectivePrecedence + " " +
                          GenerationPromptContext.BrandConstraint + " " +
                          AttributionPolicy.PromptConstraint + " " +
                          "Respect the primary derived idea and adapt it to the client context. " +
                          "imagePromptHint must describe ONLY the photographic/visual scene — subject, palette, " +
                          "lighting, mood, decorative motifs — for an AI image model to paint as a background. " +
                          "It may reference the client's palette and decorative visual notes for mood, but must " +
                          "never describe literal text, typography, wording, captions, logos, or lettering as " +
                          "something that appears IN the image: the image model cannot render legible text, and " +
                          "all real copy is rendered separately as an editable layer on top. " +
                          GenerationPromptContext.ConcisionConstraint + " " +
                          $"Carousel scope: one concept across {slideScopeHint}; no exhaustive lists. " +
                          "CTA RULE: do NOT add a call-to-action by default. Set ctaText ONLY when the user's request/" +
                          "objection explicitly asks for one, or a brand-manual directive explicitly defines one. NEVER " +
                          "infer or invent a CTA from the analysis or the client's style — when it is not explicitly asked " +
                          "for, return ctaText = null. " +
                          aestheticSuffix +
                          highlightRule +
                          "Respond with ONLY the raw JSON matching the expected structure.",
            format = "carousel",
            analysis = GenerationPromptContext.FromAnalysis(ctx.Analysis, ctx.RegenerationObjection),
            clientContext = GenerationPromptContext.FromBrand(ctx.BrandSnapshot),
            // client-style-fidelity CSF-CG-06: the highlight fields exist only for clients who highlight,
            // so every other client's prompt stays exactly as before.
            expectedOutput = highlightsWords
                ? (object)new
                {
                    coverTitle = "string - max ~6 words, punchy headline",
                    coverTitleHighlight = "string - exact words of coverTitle to emphasise",
                    slides = "array of {title: string - max ~6 words, titleHighlight: string - exact words of title to emphasise, body: string|null - max ~2 short lines, imagePromptHint: string|null}",
                    ctaText = CtaTextShape,
                    ctaLink = CtaLinkShape,
                    publicationDescription = PublicationDescriptionShape,
                    hashtags = HashtagsShape,
                }
                : new
                {
                    coverTitle = "string - max ~6 words, punchy headline",
                    slides = "array of {title: string - max ~6 words, body: string|null - max ~2 short lines, imagePromptHint: string|null}",
                    ctaText = CtaTextShape,
                    ctaLink = CtaLinkShape,
                    publicationDescription = PublicationDescriptionShape,
                    hashtags = HashtagsShape,
                }
        };

        return JsonSerializer.Serialize(input, JsonOptions);
    }

    /// <summary>
    /// True when the client was observed emphasising words in a highlight colour (an observed pairing
    /// carrying one). A colour derived from the palette is not evidence of that habit.
    /// </summary>
    internal static bool ClientHighlightsWords(ClientAestheticProfile? profile) =>
        profile?.ColorPairings?.Any(p => p.IsObserved && !string.IsNullOrWhiteSpace(p.AccentTextHex)) == true;
}
