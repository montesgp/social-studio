using SocialStudio.Infrastructure.Social.Providers.Apify;

namespace SocialStudio.Tests.Infrastructure.Social.ProfileAnalysis;

/// <summary>
/// Phase 2 (CAG2-PA-01): the scrape mapping captures per-post image URLs (cover + carousel children)
/// and caption when the actor returns them, and degrades to null when it does not.
/// </summary>
public sealed class ApifyProfileScrapeAdapterImageCaptureTests
{
    private const string WithMedia = """
    [{
      "resultType": "profile",
      "username": "client",
      "id": "999",
      "followersCount": 10,
      "latestPosts": [{
        "postId": "p1",
        "type": "Sidecar",
        "ownerUsername": "client",
        "caption": "summer drop",
        "displayUrl": "https://cdn/cover.jpg",
        "childPosts": [
          { "displayUrl": "https://cdn/c1.jpg" },
          { "displayUrl": "https://cdn/c2.jpg" }
        ]
      }]
    }]
    """;

    private const string MetadataOnly = """
    [{
      "resultType": "profile",
      "username": "client",
      "id": "999",
      "followersCount": 10,
      "latestPosts": [{
        "postId": "p2",
        "type": "Image",
        "ownerUsername": "client"
      }]
    }]
    """;

    [Fact]
    public void CapturesCoverChildImagesAndCaption()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();

        var result = adapter.MapProfileResponse(WithMedia);

        Assert.NotNull(result);
        var post = Assert.Single(result!.AllPosts);
        Assert.NotNull(post.ImageUrls);
        Assert.Equal(3, post.ImageUrls!.Count);
        Assert.Equal("https://cdn/cover.jpg", post.ImageUrls[0]);
        Assert.Contains("https://cdn/c1.jpg", post.ImageUrls);
        Assert.Contains("https://cdn/c2.jpg", post.ImageUrls);
        Assert.Equal("summer drop", post.Caption);
    }

    [Fact]
    public void MetadataOnlyActor_LeavesImageUrlsAndCaptionNull()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();

        var result = adapter.MapProfileResponse(MetadataOnly);

        Assert.NotNull(result);
        var post = Assert.Single(result!.AllPosts);
        Assert.Null(post.ImageUrls);
        Assert.Null(post.Caption);
    }
}
