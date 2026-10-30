using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Observability;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.Logging;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Formats.Hook;

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
        var input = new
        {
            instruction = "Generate branded hooks leveraging the communication patterns. " +
                          GenerationPromptContext.BrandConstraint + " " +
                          "Respect the primary derived idea and client strategy. " +
                          "Respond with ONLY raw JSON.",
            format = "hook",
            analysis = GenerationPromptContext.FromAnalysis(ctx.Analysis, ctx.RegenerationObjection),
            clientContext = GenerationPromptContext.FromBrand(ctx.BrandSnapshot),
            expectedOutput = new
            {
                primaryHook = "string - best hook",
                alternates = "array of string - alternative hooks",
                tone = "string - tone description",
                pattern = "string - which communication pattern from PatternSet this leverages"
            }
        };
        return JsonSerializer.Serialize(input, JsonOptions);
    }
}
