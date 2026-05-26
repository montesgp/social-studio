using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Caption;

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
            instruction = "Generate branded social media captions. Respond with ONLY raw JSON.",
            format = "caption",
            analysis = new { summary = ctx.Analysis.Summary, hooks = ctx.Analysis.DetectedHooks, audience = ctx.Analysis.LikelyAudience },
            brand = new
            {
                voice = ctx.BrandSnapshot.Brand?.VoiceAndTone,
                tone = ctx.BrandSnapshot.Strategy?.ToneKeywords,
                audience = ctx.BrandSnapshot.Strategy?.TargetAudience
            },
            expectedOutput = new
            {
                primaryCaption = "string — main caption",
                alternates = "array of string — alternate versions",
                hashtags = "array of string — relevant hashtags",
                mentions = "array of string|null — optional account mentions"
            }
        };
        return JsonSerializer.Serialize(input, JsonOptions);
    }
}
