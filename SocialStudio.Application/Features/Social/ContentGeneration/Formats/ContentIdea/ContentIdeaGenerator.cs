using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Observability;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.Logging;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Formats.ContentIdea;

/// <summary>Format generator for content ideas derived from analysis.</summary>
public sealed class ContentIdeaGenerator : FormatContentGeneratorBase<ContentIdeaContent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ContentIdeaGenerator(
        IAiCompletionGateway aiGateway,
        ProviderCallContext providerCallContext,
        ILogger<ContentIdeaGenerator>? logger = null)
        : base(aiGateway, providerCallContext, logger) { }

    public override string FormatKey => "content-idea";
    public override string PromptVersion => ContentGenerationPromptVersions.ContentIdea;

    protected override string BuildPromptJson(GenerationContext ctx)
    {
        var input = new
        {
            instruction = "Generate a branded content idea. " +
                          GenerationPromptContext.BrandConstraint + " " +
                          "Use the analyzed source idea as the base; do not drift away from it. " +
                          "Respond with ONLY raw JSON.",
            format = "content-idea",
            analysis = GenerationPromptContext.FromAnalysis(ctx.Analysis, ctx.RegenerationObjection),
            clientContext = GenerationPromptContext.FromBrand(ctx.BrandSnapshot),
            expectedOutput = new
            {
                ideaTitle = "string",
                angle = "string - unique angle or framing",
                platforms = "array of string platform names",
                rationale = "string - why this idea works",
                brandFitScore = "number 1-5 - how well it fits brand strategy"
            }
        };
        return JsonSerializer.Serialize(input, JsonOptions);
    }
}
