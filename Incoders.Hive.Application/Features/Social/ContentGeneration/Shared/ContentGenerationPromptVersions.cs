namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Centralised prompt-version constants for all content generation format handlers.
/// Each constant MUST be persisted with every generation record and every usage event
/// produced by the corresponding format handler.
/// </summary>
public static class ContentGenerationPromptVersions
{
    public const string Carousel = "carousel.v1";
    public const string Flyer = "flyer.v1";
    public const string ReelScript = "reel-script.v1";
    public const string ContentIdea = "content-idea.v1";
    public const string CtaVariation = "cta-variation.v1";
    public const string Caption = "caption.v1";
    public const string Hook = "hook.v1";
}
