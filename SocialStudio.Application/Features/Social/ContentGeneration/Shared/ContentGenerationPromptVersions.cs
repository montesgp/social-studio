namespace SocialStudio.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Centralised prompt-version constants for all content generation format handlers.
/// Each constant MUST be persisted with every generation record and every usage event
/// produced by the corresponding format handler.
/// </summary>
public static class ContentGenerationPromptVersions
{
    // WU-7: bumped after adding ConcisionConstraint to all three generators (CG-CON-04).
    // WU-4 (CAG-CG-11): bumped after adding additive AestheticGuidance to all three generators.
    // v5/v4: conditional CTA — never invent a call-to-action unless the client actually uses one.
    // v6/v4/v5: client-directive precedence (particular→general) — brand-manual notes are authoritative
    //           and override defaults (incl. prohibitions); style inference is the fallback for signature.
    //           CTA is default-OFF: added ONLY on explicit user request or an explicit brand-manual
    //           directive — never inferred from the analysis/style (the agent must not invent CTAs).
    // v7/v5/v6: post caption bundled in-call — publicationDescription is now mandatory (never null) and a
    //           hashtags[] array was added to the visual generators, so the studio post carries its caption
    //           without a separate caption call.
    // v8 (carousel): optional titleHighlight / coverTitleHighlight for clients observed highlighting
    //           one key phrase per headline (client-style-fidelity CSF-CG-06).
    public const string Carousel = "carousel.v8";
    public const string Story = "story-single.v5";
    public const string FeedPost = "feed-single.v6";
    public const string ReelScript = "reel-script.v2";
    public const string ContentIdea = "content-idea.v2";
    public const string CtaVariation = "cta-variation.v2";
    public const string Caption = "caption.v2";
    public const string Hook = "hook.v2";
}
