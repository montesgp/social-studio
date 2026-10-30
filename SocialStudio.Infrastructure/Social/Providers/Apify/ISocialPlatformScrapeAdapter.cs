using System.Text.Json;
using SocialStudio.Application.Abstractions.Social;

namespace SocialStudio.Infrastructure.Social.Providers.Apify;

/// <summary>
/// Per-platform adapter that knows how to build the Apify actor input and map one dataset item
/// to a <see cref="SocialContentSnapshot"/>. Implementations stay in Infrastructure — no provider
/// or JSON types leak into Application or Domain.
/// </summary>
internal interface ISocialPlatformScrapeAdapter
{
    /// <summary>
    /// The lowercase platform key this adapter handles (e.g. "tiktok", "instagram", "default").
    /// The default adapter uses "default" and acts as the catch-all fallback.
    /// </summary>
    string PlatformKey { get; }

    /// <summary>
    /// Builds the actor input object that will be JSON-serialized and sent as the Apify run body.
    /// </summary>
    object BuildActorInput(SocialContentRequest request);

    /// <summary>
    /// Maps a single Apify dataset item JSON element to a platform-neutral <see cref="SocialContentSnapshot"/>.
    /// </summary>
    SocialContentSnapshot MapItem(JsonElement item, SocialContentRequest request);
}
