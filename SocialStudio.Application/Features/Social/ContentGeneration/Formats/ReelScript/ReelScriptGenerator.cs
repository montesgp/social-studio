using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Observability;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.Logging;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Formats.ReelScript;

/// <summary>Format generator for Reel/TikTok video scripts.</summary>
public sealed class ReelScriptGenerator : FormatContentGeneratorBase<ReelScriptContent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ReelScriptGenerator(
        IAiCompletionGateway aiGateway,
        ProviderCallContext providerCallContext,
        ILogger<ReelScriptGenerator>? logger = null)
        : base(aiGateway, providerCallContext, logger) { }

    public override string FormatKey => "reel-script";
    public override string PromptVersion => ContentGenerationPromptVersions.ReelScript;

    protected override string BuildPromptJson(GenerationContext ctx)
    {
        var input = new
        {
            instruction = "Generate a branded reel/TikTok script. " +
                          GenerationPromptContext.BrandConstraint + " " +
                          "Respect the primary derived idea and the client's strategy/objectives. " +
                          "Respond with ONLY raw JSON.",
            format = "reel-script",
            analysis = GenerationPromptContext.FromAnalysis(ctx.Analysis, ctx.RegenerationObjection),
            clientContext = GenerationPromptContext.FromBrand(ctx.BrandSnapshot),
            expectedOutput = new
            {
                hook = "string - opening hook line",
                scenes = "array of {timestamp: string, voiceover: string, onScreenText: string}",
                ctaText = "string",
                suggestedMusicMood = "string|null"
            }
        };
        return JsonSerializer.Serialize(input, JsonOptions);
    }
}
