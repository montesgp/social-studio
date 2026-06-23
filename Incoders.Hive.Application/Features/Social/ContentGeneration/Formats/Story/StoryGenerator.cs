using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Story;

/// <summary>Format generator for story content (single 9:16 vertical image).</summary>
public sealed class StoryGenerator : FormatContentGeneratorBase<StoryContent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public StoryGenerator(
        IAiCompletionGateway aiGateway,
        ProviderCallContext providerCallContext,
        ILogger<StoryGenerator>? logger = null)
        : base(aiGateway, providerCallContext, logger) { }

    public override string FormatKey => "story-single";
    public override string PromptVersion => ContentGenerationPromptVersions.Story;

    protected override string BuildPromptJson(GenerationContext ctx)
    {
        // WU-4 (CAG-CG-08): aesthetic guidance appended AFTER ConcisionConstraint (additive, advisory).
        // StoryGenerator does NOT emit the avg-slides hint (carousel-only advisory per design §6).
        // Non-CaseC jobs: ctx.AestheticProfile is null → AestheticGuidance returns "" → no change.
        var aestheticGuidance = GenerationPromptContext.AestheticGuidance(
            ctx.AestheticProfile is { AverageSlidesPerCarousel: not null }
                ? ctx.AestheticProfile with { AverageSlidesPerCarousel = null }
                : ctx.AestheticProfile);
        var aestheticSuffix = aestheticGuidance.Length > 0 ? " " + aestheticGuidance : string.Empty;

        var input = new
        {
            instruction = "Generate branded story content. " +
                          GenerationPromptContext.ClientDirectivePrecedence + " " +
                          GenerationPromptContext.BrandConstraint + " " +
                          AttributionPolicy.PromptConstraint + " " +
                          "Respect the primary derived idea and make layout/copy decisions from the client context. " +
                          "The story is a single 9:16 story image. If the active brand manual includes " +
                          "backgroundTemplates, use them as the visual base and write copy that fits over that template. " +
                          "If it has no background template, resolve a concise backgroundPrompt for a branded AI-created background. " +
                          "SIGNATURE RULE: set signatureText only when the client directives require a signature/handle, or " +
                          "(when silent) the client consistently signs their own posts; otherwise null — never invent one. " +
                          GenerationPromptContext.ConcisionConstraint + " " +
                          "Singles super-summary framing: one bold headline, minimal supporting copy — no paragraphs. " +
                          "CTA RULE: do NOT add a call-to-action by default. Set ctaText ONLY when the user's request/" +
                          "objection explicitly asks for one, or a brand-manual directive explicitly defines one. NEVER " +
                          "infer or invent a CTA from the analysis or the client's style — otherwise return ctaText = null. " +
                          aestheticSuffix +
                          "Respond with ONLY raw JSON.",
            format = "story-single",
            analysis = GenerationPromptContext.FromAnalysis(ctx.Analysis, ctx.RegenerationObjection),
            clientContext = GenerationPromptContext.FromBrand(ctx.BrandSnapshot),
            expectedOutput = new
            {
                title = "string - main headline",
                subtitle = "string|null",
                body = "string|null",
                callouts = "array of string bullet points",
                ctaText = "string|null - ONLY when explicitly requested by the user or defined by a brand-manual directive; null otherwise — never inferred or invented",
                backgroundPrompt = "string|null - visual background direction when no brand template exists",
                signatureText = "string|null - signature/handle ONLY when client directives require it or the client consistently signs; null otherwise",
                layoutHint = "string|null - layout suggestion e.g. grid, hero, split",
                publicationDescription = "string|null - social post copy to publish with this story"
            }
        };
        return JsonSerializer.Serialize(input, JsonOptions);
    }
}
