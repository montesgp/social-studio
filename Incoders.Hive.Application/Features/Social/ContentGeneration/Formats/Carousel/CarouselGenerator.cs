using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;

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

    /// <inheritdoc />
    public override string FormatKey => "carousel";

    /// <inheritdoc />
    public override string PromptVersion => ContentGenerationPromptVersions.Carousel;

    /// <inheritdoc />
    protected override string BuildPromptJson(GenerationContext ctx)
    {
        // WU-4 (CAG-CG-08): aesthetic guidance appended AFTER ConcisionConstraint (additive, advisory).
        // Only CarouselGenerator emits the avg-slides hint (carousel-only advisory per design §6).
        // Non-CaseC jobs: ctx.AestheticProfile is null → AestheticGuidance returns "" → no change.
        var aestheticGuidance = GenerationPromptContext.AestheticGuidance(ctx.AestheticProfile);
        var aestheticSuffix = aestheticGuidance.Length > 0 ? " " + aestheticGuidance : string.Empty;

        var input = new
        {
            instruction = "Generate a branded carousel post with a cover slide and content slides. " +
                          GenerationPromptContext.ClientDirectivePrecedence + " " +
                          GenerationPromptContext.BrandConstraint + " " +
                          AttributionPolicy.PromptConstraint + " " +
                          "Respect the primary derived idea and adapt it to the client context. " +
                          "Image prompt hints must reference the client's explicit palette, typography, logos, and visual notes when available. " +
                          GenerationPromptContext.ConcisionConstraint + " " +
                          "Carousel scope: one concept across ~3-5 slides; no exhaustive lists. " +
                          "CTA RULE: do NOT add a call-to-action by default. Set ctaText ONLY when the user's request/" +
                          "objection explicitly asks for one, or a brand-manual directive explicitly defines one. NEVER " +
                          "infer or invent a CTA from the analysis or the client's style — when it is not explicitly asked " +
                          "for, return ctaText = null. " +
                          aestheticSuffix +
                          "Respond with ONLY the raw JSON matching the expected structure.",
            format = "carousel",
            analysis = GenerationPromptContext.FromAnalysis(ctx.Analysis, ctx.RegenerationObjection),
            clientContext = GenerationPromptContext.FromBrand(ctx.BrandSnapshot),
            expectedOutput = new
            {
                coverTitle = "string - max ~6 words, punchy headline",
                slides = "array of {title: string - max ~6 words, body: string|null - max ~2 short lines, imagePromptHint: string|null}",
                ctaText = "string|null - ONLY when explicitly requested by the user or defined by a brand-manual directive; null otherwise — never inferred or invented",
                ctaLink = "string|null - optional URL for the CTA",
                publicationDescription = "string|null - social post copy to publish with this carousel"
            }
        };

        return JsonSerializer.Serialize(input, JsonOptions);
    }
}
