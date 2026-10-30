using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Observability;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.Logging;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Formats.Caption;

/// <summary>Format generator for social media captions.</summary>
public sealed class CaptionGenerator : FormatContentGeneratorBase<CaptionContent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CaptionGenerator(
        IAiCompletionGateway aiGateway,
        ProviderCallContext providerCallContext,
        ILogger<CaptionGenerator>? logger = null)
        : base(aiGateway, providerCallContext, logger) { }

    public override string FormatKey => "caption";
    public override string PromptVersion => ContentGenerationPromptVersions.Caption;

    protected override string BuildPromptJson(GenerationContext ctx)
    {
        var input = new
        {
            instruction = "Generate branded social media captions. " +
                          GenerationPromptContext.BrandConstraint + " " +
                          "Respect the primary derived idea, the client's voice, audience, objectives, and KPIs. " +
                          "Respond with ONLY raw JSON.",
            format = "caption",
            analysis = GenerationPromptContext.FromAnalysis(ctx.Analysis, ctx.RegenerationObjection),
            clientContext = GenerationPromptContext.FromBrand(ctx.BrandSnapshot),
            expectedOutput = new
            {
                primaryCaption = "string - main caption",
                alternates = "array of string - alternate versions",
                hashtags = "array of string - relevant hashtags",
                mentions = "array of string|null - optional account mentions"
            }
        };
        return JsonSerializer.Serialize(input, JsonOptions);
    }
}
