namespace Incoders.Hive.Infrastructure.Ai.Providers;

/// <summary>
/// Shared image-generation prompt directive text referenced by every
/// <c>IImageModelClient</c> adapter, so provider-specific prompt wording cannot drift between
/// adapters that must express the same semantics (CAGH-CUI-01-c; design D5).
/// </summary>
internal static class ImagePromptDirectives
{
    /// <summary>
    /// Directive injected when <c>ImageGenerationRequest.BackgroundOnly</c> is true (CAC-IMG-02,
    /// CAGH-CUI-01): tells the provider to generate only the background layer. Moved verbatim from
    /// <c>OpenAiImageModelClient.BackgroundOnlyDirective</c> (D5); referenced identically by
    /// <c>OpenAiImageModelClient</c> and <c>ComfyUiImageModelClient</c> so the two adapters cannot
    /// drift.
    /// </summary>
    internal const string BackgroundOnly =
        "generate ONLY the background; never render text, logos, @handles, or signatures";
}
