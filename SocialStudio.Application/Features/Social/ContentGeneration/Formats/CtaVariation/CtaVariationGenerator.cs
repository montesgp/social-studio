using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Observability;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.Logging;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Formats.CtaVariation;

/// <summary>Format generator for CTA variations.</summary>
public sealed class CtaVariationGenerator : FormatContentGeneratorBase<CtaVariationContent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CtaVariationGenerator(
        IAiCompletionGateway aiGateway,
        ProviderCallContext providerCallContext,
        ILogger<CtaVariationGenerator>? logger = null)
        : base(aiGateway, providerCallContext, logger) { }

    public override string FormatKey => "cta-variation";
    public override string PromptVersion => ContentGenerationPromptVersions.CtaVariation;

    protected override string BuildPromptJson(GenerationContext ctx)
    {
        var input = new
        {
            instruction = "Generate branded CTA variations. " +
                          GenerationPromptContext.BrandConstraint + " " +
                          "Tie each CTA to the client's objectives and KPIs when present. " +
                          "Respond with ONLY raw JSON.",
            format = "cta-variation",
            analysis = GenerationPromptContext.FromAnalysis(ctx.Analysis, ctx.RegenerationObjection),
            clientContext = GenerationPromptContext.FromBrand(ctx.BrandSnapshot),
            expectedOutput = new
            {
                variations = "array of {text: string, tone: string, intent: string}"
            }
        };
        return JsonSerializer.Serialize(input, JsonOptions);
    }
}
