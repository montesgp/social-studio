using SocialStudio.Domain.Social.ContentFormats;
using SocialStudio.Domain.Social.ContentStudio;

namespace SocialStudio.Application.Features.Social.ContentStudio.Materialize;

/// <summary>
/// Maps a generation format key to its preview/edit medium and a default destination channel.
/// Delegates to <see cref="FormatCatalog.ResolveForAssembly"/> for catalog-known keys
/// (including the legacy "carousel" alias on the read path) and falls back to a static
/// switch for non-catalog formats.
/// Open by design — unknown formats default to editable text on the Instagram feed.
/// </summary>
public static class ContentFormatCatalog
{
    /// <summary>
    /// Returns the <see cref="ContentMedium"/> for the given format key.
    /// Catalog-known keys (and legacy "carousel") resolve via <see cref="FormatCatalog"/>;
    /// non-catalog variants fall back to the static classification below.
    /// </summary>
    public static ContentMedium MediumFor(string formatKey)
    {
        // Catalog-aware lookup: resolves canonical keys + legacy "carousel" alias.
        var def = FormatCatalog.ResolveForAssembly(Normalize(formatKey));
        if (def is not null)
        {
            return def.ContentMedium;
        }

        // Non-catalog fallback for legacy/external format identifiers.
        return Normalize(formatKey) switch
        {
            "slide" or "slides" or "thumbnail" or "post" => ContentMedium.Visual,
            "reel" or "short" or "video" => ContentMedium.Video,
            "presentation" or "pptx" or "deck" => ContentMedium.Document,
            _ => ContentMedium.Text, // caption, hook, idea(s), cta, reel-script, markdown-script, script...
        };
    }

    /// <summary>
    /// Returns the default <see cref="TargetChannel"/> for the given format key.
    /// Catalog-known keys (and legacy "carousel") resolve via <see cref="FormatCatalog"/>;
    /// non-catalog variants fall back to the static mapping below.
    /// </summary>
    public static TargetChannel ChannelFor(string formatKey)
    {
        // Catalog-aware lookup: resolves canonical keys + legacy "carousel" alias.
        var def = FormatCatalog.ResolveForAssembly(Normalize(formatKey));
        if (def is not null)
        {
            return def.Channel;
        }

        // Non-catalog fallback for legacy/external format identifiers.
        return Normalize(formatKey) switch
        {
            "reel" => new TargetChannel("instagram", "reel"),
            "short" => new TargetChannel("youtube", "short"),
            "tiktok" => new TargetChannel("tiktok", "feed"),
            _ => new TargetChannel("instagram", "feed"),
        };
    }

    private static string Normalize(string formatKey) =>
        (formatKey ?? string.Empty).Trim().ToLowerInvariant();
}
