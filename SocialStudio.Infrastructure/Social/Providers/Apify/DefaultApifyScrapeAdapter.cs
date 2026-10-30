using System.Text.Json;
using SocialStudio.Application.Abstractions.Social;

namespace SocialStudio.Infrastructure.Social.Providers.Apify;

/// <summary>
/// Fallback adapter that preserves the legacy <c>{ startUrls }</c> input shape and delegates
/// to <see cref="SocialSnapshotMapper"/> for response mapping.
/// Used for unknown platforms (e.g. Data365 usage, youtube stubs) to maintain back-compat.
/// </summary>
internal sealed class DefaultApifyScrapeAdapter : ISocialPlatformScrapeAdapter
{
    public string PlatformKey => "default";

    public object BuildActorInput(SocialContentRequest request) => new
    {
        startUrls = new[] { new { url = request.NormalizedUrl } }
    };

    public SocialContentSnapshot MapItem(JsonElement item, SocialContentRequest request)
    {
        // Re-use the existing generic mapper for back-compat with legacy/unknown actors.
        // Serialize the single item back to JSON and pass to the mapper.
        var singleItemJson = $"[{item}]";
        return SocialSnapshotMapper.FromJson(
            request.PlatformKey,
            request.NormalizedUrl,
            singleItemJson,
            rawPayloadReference: "provider://apify/run-sync");
    }
}
