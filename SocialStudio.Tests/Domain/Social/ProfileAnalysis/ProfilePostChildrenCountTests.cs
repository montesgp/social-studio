using SocialStudio.Domain.Social.ProfileAnalysis;

namespace SocialStudio.Tests.Domain.Social.ProfileAnalysis;

/// <summary>
/// TDD tests for WU-1 task 1.1/1.2: ProfilePost.CarouselChildrenCount (CAG-PA-01).
/// </summary>
public sealed class ProfilePostChildrenCountTests
{
    private static readonly DateTimeOffset Timestamp = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

    // -----------------------------------------------------------------------
    // Field existence + null for non-carousel posts
    // -----------------------------------------------------------------------

    [Fact]
    public void ProfilePost_ExposeCarouselChildrenCount_AsNullableInt()
    {
        var post = CreatePost(mediaType: "Image", carouselChildrenCount: null);

        int? count = post.CarouselChildrenCount;

        Assert.Null(count);
    }

    [Fact]
    public void ProfilePost_CarouselChildrenCount_IsNull_ForNonCarouselPost()
    {
        var imagePost = CreatePost(mediaType: "Image", carouselChildrenCount: null);
        var videoPost = CreatePost(mediaType: "Video", carouselChildrenCount: null);

        Assert.Null(imagePost.CarouselChildrenCount);
        Assert.Null(videoPost.CarouselChildrenCount);
    }

    [Fact]
    public void ProfilePost_ConstructionSucceeds_WithNullCarouselChildrenCount()
    {
        var post = CreatePost(mediaType: "Carousel", carouselChildrenCount: null);

        Assert.NotNull(post);
        Assert.Null(post.CarouselChildrenCount);
    }

    [Fact]
    public void ProfilePost_CarouselChildrenCount_StoresValue_ForCarouselPost()
    {
        var post = CreatePost(mediaType: "Carousel", carouselChildrenCount: 6);

        Assert.Equal(6, post.CarouselChildrenCount);
    }

    [Fact]
    public void ProfilePost_CarouselChildrenCount_IsIndependentOfMediaType()
    {
        // The field is purely informational; the caller decides when it is meaningful.
        var post = CreatePost(mediaType: "Carousel", carouselChildrenCount: 3);

        Assert.Equal(3, post.CarouselChildrenCount);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ProfilePost CreatePost(string mediaType, int? carouselChildrenCount) =>
        new ProfilePost(
            PostId: "post_001",
            MediaType: mediaType,
            LikesCount: 100,
            CommentsCount: 5,
            VideoViewCount: null,
            Timestamp: Timestamp,
            OwnerId: "12345",
            OwnerUsername: "testaccount",
            IsPaidPartnership: false,
            IsDegraded: false,
            Hashtags: [],
            CarouselChildrenCount: carouselChildrenCount);
}
