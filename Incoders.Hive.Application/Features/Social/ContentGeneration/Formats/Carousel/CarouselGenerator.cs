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
        var brand = ctx.BrandSnapshot;
        var analysis = ctx.Analysis;

        var input = new
        {
            instruction = "Generate a branded carousel post with a cover slide, content slides, and a CTA. " +
                          "Respond with ONLY the raw JSON matching the expected structure.",
            format = "carousel",
            analysis = new
            {
                summary = analysis.Summary,
                audience = analysis.LikelyAudience,
                hooks = analysis.DetectedHooks,
                strengths = analysis.Strengths,
                adaptationRecommendations = analysis.ClientAdaptationRecommendations,
                patterns = analysis.Patterns is not null
                    ? (object)new
                    {
                        communication = analysis.Patterns.CommunicationPatterns.Select(p => p.Name),
                        emotional = analysis.Patterns.EmotionalPatterns.Select(p => p.Name),
                        conversion = analysis.Patterns.ConversionPatterns.Select(p => p.Name)
                    }
                    : null
            },
            brand = new
            {
                colors = brand.Brand?.ColorPalette,
                voice = brand.Brand?.VoiceAndTone,
                typography = brand.Brand?.Typography,
                pillars = brand.Strategy?.ContentPillars,
                toneKeywords = brand.Strategy?.ToneKeywords,
                targetAudience = brand.Strategy?.TargetAudience
            },
            expectedOutput = new
            {
                coverTitle = "string — engaging cover headline",
                slides = "array of {title: string, body: string, imagePromptHint: string|null}",
                ctaText = "string — call to action text",
                ctaLink = "string|null — optional URL for the CTA"
            }
        };

        return JsonSerializer.Serialize(input, JsonOptions);
    }
}
