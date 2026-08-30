using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Tests.Domain.Social.ProfileAnalysis;

/// <summary>Phase 2 (CAG2-PA-01): ProfilePost carries optional captured media URLs + caption.</summary>
public sealed class ProfilePostMediaFieldsTests
{
    private static ProfilePost Base() => new(
        PostId: "p1",
        MediaType: "Sidecar",
        LikesCount: 10,
        CommentsCount: 2,
        VideoViewCount: null,
        Timestamp: DateTimeOffset.UtcNow,
        OwnerId: "owner-1",
        OwnerUsername: "client",
        IsPaidPartnership: false,
        IsDegraded: false,
        Hashtags: []);

    [Fact]
    public void Defaults_AreNull_WhenActorReturnsNoMedia()
    {
        var post = Base();

        Assert.Null(post.ImageUrls);
        Assert.Null(post.Caption);
    }

    [Fact]
    public void CapturesCoverAndCarouselChildImageUrls()
    {
        var post = Base() with
        {
            ImageUrls = ["https://cdn/cover.jpg", "https://cdn/child-1.jpg", "https://cdn/child-2.jpg"],
            Caption = "summer drop ☀️",
        };

        Assert.Equal(3, post.ImageUrls!.Count);
        Assert.Equal("https://cdn/cover.jpg", post.ImageUrls[0]);
        Assert.Equal("summer drop ☀️", post.Caption);
    }

    [Fact]
    public void Phase1PositionalConstruction_StillCompiles_NoBreakingChange()
    {
        // The first 11 positional args + optional CarouselChildrenCount must still construct.
        var post = new ProfilePost(
            "p2", "Image", 1, 0, null, DateTimeOffset.UtcNow,
            "owner-1", "client", false, false, [], 0);

        Assert.Equal("p2", post.PostId);
        Assert.Null(post.ImageUrls);
        Assert.Null(post.Caption);
    }
}
