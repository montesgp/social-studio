using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.FeedPost;

/// <summary>Format generator for static feed post content (single image for social feed).</summary>
public sealed class FeedPostGenerator : FormatContentGeneratorBase<FeedPostContent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FeedPostGenerator(
        IAiCompletionGateway aiGateway,
        ProviderCallContext providerCallContext,
        ILogger<FeedPostGenerator>? logger = null)
        : base(aiGateway, providerCallContext, logger) { }

    public override string FormatKey => "feed-post";
    public override string PromptVersion => ContentGenerationPromptVersions.FeedPost;

    protected override string BuildPromptJson(GenerationContext ctx)
    {
        var input = new
        {
            instruction = "Generate branded feed post content. " +
                          GenerationPromptContext.BrandConstraint + " " +
                          "Respect the primary derived idea and make layout/copy decisions from the client context. " +
                          "The feed post is a single static image for the social media feed (not ephemeral). " +
                          "If the active brand manual includes backgroundTemplates, use them as the visual base and write copy that fits over that template. " +
                          "If it has no background template, resolve a concise backgroundPrompt for a branded AI-created background. " +
                          "Apply any manual notes such as required signatures or handles in signatureText. " +
                          "Respond with ONLY raw JSON.",
            format = "feed-post",
            analysis = GenerationPromptContext.FromAnalysis(ctx.Analysis, ctx.RegenerationObjection),
            clientContext = GenerationPromptContext.FromBrand(ctx.BrandSnapshot),
            expectedOutput = new
            {
                title = "string - main headline",
                subtitle = "string|null",
                body = "string|null",
                callouts = "array of string bullet points",
                ctaText = "string",
                backgroundPrompt = "string|null - visual background direction when no brand template exists",
                signatureText = "string|null - required footer/signature/handle from brand manual when applicable",
                layoutHint = "string|null - layout suggestion e.g. grid, hero, split",
                publicationDescription = "string|null - social post copy to publish with this feed post"
            }
        };
        return JsonSerializer.Serialize(input, JsonOptions);
    }
}
