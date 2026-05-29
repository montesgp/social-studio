using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Flyer;

/// <summary>Format generator for promotional flyer content.</summary>
public sealed class FlyerGenerator : FormatContentGeneratorBase<FlyerContent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FlyerGenerator(
        IAiCompletionGateway aiGateway,
        ProviderCallContext providerCallContext,
        ILogger<FlyerGenerator>? logger = null)
        : base(aiGateway, providerCallContext, logger) { }

    public override string FormatKey => "flyer";
    public override string PromptVersion => ContentGenerationPromptVersions.Flyer;

    protected override string BuildPromptJson(GenerationContext ctx)
    {
        var input = new
        {
            instruction = "Generate branded flyer content. Respond with ONLY raw JSON.",
            format = "flyer",
            analysis = new { summary = ctx.Analysis.Summary, audience = ctx.Analysis.LikelyAudience },
            brand = new { voice = ctx.BrandSnapshot.Brand?.VoiceAndTone, colors = ctx.BrandSnapshot.Brand?.ColorPalette },
            expectedOutput = new
            {
                title = "string — main headline",
                subtitle = "string|null",
                body = "string|null",
                callouts = "array of string bullet points",
                ctaText = "string",
                layoutHint = "string|null — layout suggestion e.g. grid, hero, split"
            }
        };
        return JsonSerializer.Serialize(input, JsonOptions);
    }
}
