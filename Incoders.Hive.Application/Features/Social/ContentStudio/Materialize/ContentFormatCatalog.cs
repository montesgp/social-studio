using Incoders.Hive.Domain.Social.ContentStudio;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Materialize;

/// <summary>
/// Maps a generation format key to its preview/edit medium and a default destination channel.
/// Open by design — unknown formats default to editable text on the Instagram feed.
/// </summary>
public static class ContentFormatCatalog
{
    public static ContentMedium MediumFor(string formatKey) => Normalize(formatKey) switch
    {
        "flyer" or "carousel" or "slide" or "slides" or "story" or "thumbnail" or "post" => ContentMedium.Visual,
        "reel" or "short" or "video" => ContentMedium.Video,
        "presentation" or "pptx" or "deck" => ContentMedium.Document,
        _ => ContentMedium.Text, // caption, hook, idea(s), cta, reel-script, markdown-script, script...
    };

    public static TargetChannel ChannelFor(string formatKey) => Normalize(formatKey) switch
    {
        "reel" or "reel-script" => new TargetChannel("instagram", "reel"),
        "story" => new TargetChannel("instagram", "story"),
        "carousel" => new TargetChannel("instagram", "carousel"),
        "short" => new TargetChannel("youtube", "short"),
        "tiktok" => new TargetChannel("tiktok", "feed"),
        _ => new TargetChannel("instagram", "feed"),
    };

    private static string Normalize(string formatKey) =>
        (formatKey ?? string.Empty).Trim().ToLowerInvariant();
}
