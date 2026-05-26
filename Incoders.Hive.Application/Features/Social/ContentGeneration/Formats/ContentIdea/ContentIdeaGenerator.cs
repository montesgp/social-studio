using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.ContentIdea;

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
            instruction = "Generate a branded content idea. Respond with ONLY raw JSON.",
            format = "content-idea",
            analysis = new
            {
                summary = ctx.Analysis.Summary,
                derivedIdeas = ctx.Analysis.DerivedIdeas.Select(i => i.Title),
                audience = ctx.Analysis.LikelyAudience
            },
            brand = new { pillars = ctx.BrandSnapshot.Strategy?.ContentPillars, audience = ctx.BrandSnapshot.Strategy?.TargetAudience },
            expectedOutput = new
            {
                ideaTitle = "string",
                angle = "string — unique angle or framing",
                platforms = "array of string platform names",
                rationale = "string — why this idea works",
                brandFitScore = "number 1-5 — how well it fits brand strategy"
            }
        };
        return JsonSerializer.Serialize(input, JsonOptions);
    }
}
