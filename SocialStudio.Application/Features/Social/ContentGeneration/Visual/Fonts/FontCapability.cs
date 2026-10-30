namespace SocialStudio.Application.Features.Social.ContentGeneration.Visual.Fonts;

/// <summary>
/// Result of probing the bundled fonts for a render-side capability.
/// </summary>
/// <param name="SupportsColorEmoji">
/// <c>true</c> when the embedded emoji typeface actually contains a glyph for a known emoji
/// codepoint (U+1F44B, WAVING HAND SIGN) — see Decision F3 in the Content Studio design. This
/// gates the slide-text emoji picker (R6.2): the picker MUST NOT be offered when this is
/// <c>false</c>, because that would ship tofu.
/// </param>
public sealed record FontCapability(bool SupportsColorEmoji);
