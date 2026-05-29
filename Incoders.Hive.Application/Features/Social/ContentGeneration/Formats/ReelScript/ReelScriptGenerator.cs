using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.ReelScript;

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
            instruction = "Generate a branded reel/TikTok script. Respond with ONLY raw JSON.",
            format = "reel-script",
            analysis = new
            {
                summary = ctx.Analysis.Summary,
                hooks = ctx.Analysis.DetectedHooks,
                audience = ctx.Analysis.LikelyAudience,
                narrativeStructure = ctx.Analysis.NarrativeStructure
            },
            brand = new { voice = ctx.BrandSnapshot.Brand?.VoiceAndTone, tone = ctx.BrandSnapshot.Strategy?.ToneKeywords },
            expectedOutput = new
            {
                hook = "string — opening hook line",
                scenes = "array of {timestamp: string, voiceover: string, onScreenText: string}",
                ctaText = "string",
                suggestedMusicMood = "string|null"
            }
        };
        return JsonSerializer.Serialize(input, JsonOptions);
    }
}
