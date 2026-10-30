using SocialStudio.Application.Abstractions.Social.ContentDiscovery;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Discovery;

/// <summary>
/// WU-2 (D3): <see cref="CandidateIdentity.From"/> is the ONE pure normalization function shared by
/// the chain's in-run dedup and the persisted <c>candidate_key</c>, so the two can never disagree.
/// </summary>
public sealed class CandidateIdentityTests
{
    [Theory]
    [InlineData("tiktok", "https://www.tiktok.com/@a/video/1")]
    [InlineData("TikTok", "HTTPS://WWW.TIKTOK.COM/@a/video/1")]
    [InlineData("tiktok", "https://tiktok.com/@a/video/1/")]
    public void From_NormalizesEquivalentVariants_ToTheSameIdentity(string network, string url)
    {
        var canonical = CandidateIdentity.From("tiktok", "https://tiktok.com/@a/video/1");
        var identity = CandidateIdentity.From(network, url);

        Assert.Equal(canonical, identity);
    }

    [Fact]
    public void From_IsPrefixedByTheLowercasedNetworkKey()
    {
        var identity = CandidateIdentity.From("TikTok", "https://www.tiktok.com/@a/video/1");

        Assert.StartsWith("tiktok:", identity, StringComparison.Ordinal);
    }

    [Fact]
    public void From_DropsWwwPrefix()
    {
        var withWww = CandidateIdentity.From("tiktok", "https://www.tiktok.com/@a/video/1");
        var withoutWww = CandidateIdentity.From("tiktok", "https://tiktok.com/@a/video/1");

        Assert.Equal(withoutWww, withWww);
    }

    [Fact]
    public void From_TrimsTrailingSlash()
    {
        var withSlash = CandidateIdentity.From("instagram", "https://instagram.com/p/1/");
        var withoutSlash = CandidateIdentity.From("instagram", "https://instagram.com/p/1");

        Assert.Equal(withoutSlash, withSlash);
    }

    [Fact]
    public void From_DropsQueryAndFragment()
    {
        var plain = CandidateIdentity.From("instagram", "https://instagram.com/p/1");
        var withQuery = CandidateIdentity.From("instagram", "https://instagram.com/p/1?ref=share");
        var withFragment = CandidateIdentity.From("instagram", "https://instagram.com/p/1#top");

        Assert.Equal(plain, withQuery);
        Assert.Equal(plain, withFragment);
    }

    [Fact]
    public void From_TwoDifferentUrls_ProduceDifferentIdentities()
    {
        var first = CandidateIdentity.From("tiktok", "https://tiktok.com/@a/video/1");
        var second = CandidateIdentity.From("tiktok", "https://tiktok.com/@a/video/2");

        Assert.NotEqual(first, second);
    }
}
