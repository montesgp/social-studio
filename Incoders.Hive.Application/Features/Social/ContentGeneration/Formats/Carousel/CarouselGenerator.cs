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
        var input = new
        {
            instruction = "Generate a branded carousel post with a cover slide, content slides, and a CTA. " +
                          GenerationPromptContext.BrandConstraint + " " +
                          "Respect the primary derived idea and adapt it to the client context. " +
                          "Image prompt hints must reference the client's explicit palette, typography, logos, and visual notes when available. " +
                          "Respond with ONLY the raw JSON matching the expected structure.",
            format = "carousel",
            analysis = GenerationPromptContext.FromAnalysis(ctx.Analysis),
            clientContext = GenerationPromptContext.FromBrand(ctx.BrandSnapshot),
            expectedOutput = new
            {
                coverTitle = "string - engaging cover headline",
                slides = "array of {title: string, body: string, imagePromptHint: string|null}",
                ctaText = "string - call to action text",
                ctaLink = "string|null - optional URL for the CTA"
            }
        };

        return JsonSerializer.Serialize(input, JsonOptions);
    }
}
