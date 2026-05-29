using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Hook;

/// <summary>Format generator for attention-grabbing hooks.</summary>
public sealed class HookGenerator : FormatContentGeneratorBase<HookContent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public HookGenerator(
        IAiCompletionGateway aiGateway,
        ProviderCallContext providerCallContext,
        ILogger<HookGenerator>? logger = null)
        : base(aiGateway, providerCallContext, logger) { }

    public override string FormatKey => "hook";
    public override string PromptVersion => ContentGenerationPromptVersions.Hook;

    protected override string BuildPromptJson(GenerationContext ctx)
    {
        var analysis = ctx.Analysis;
        var input = new
        {
            instruction = "Generate branded hooks leveraging the communication patterns. Respond with ONLY raw JSON.",
            format = "hook",
            analysis = new
            {
                summary = analysis.Summary,
                hooks = analysis.DetectedHooks,
                audience = analysis.LikelyAudience,
                communicationPatterns = analysis.Patterns?.CommunicationPatterns.Select(p => p.Name)
            },
            brand = new { voice = ctx.BrandSnapshot.Brand?.VoiceAndTone, tone = ctx.BrandSnapshot.Strategy?.ToneKeywords },
            expectedOutput = new
            {
                primaryHook = "string — best hook",
                alternates = "array of string — alternative hooks",
                tone = "string — tone description",
                pattern = "string — which communication pattern from PatternSet this leverages"
            }
        };
        return JsonSerializer.Serialize(input, JsonOptions);
    }
}
