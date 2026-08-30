using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Tests.Domain.Social.ProfileAnalysis;

/// <summary>Phase 2 (CAG2-PA-02): durable post-image reference value type.</summary>
public sealed class ProfilePostImageRefTests
{
    [Fact]
    public void HoldsSourcePostIndexPathAndHash()
    {
        var reference = new ProfilePostImageRef(
            SourcePostId: "p1",
            AssetIndex: 0,
            StoragePath: "org/client/analysis/aesthetic/p1-0.jpg",
            ContentHash: "sha256:abc");

        Assert.Equal("p1", reference.SourcePostId);
        Assert.Equal(0, reference.AssetIndex);
        Assert.Equal("org/client/analysis/aesthetic/p1-0.jpg", reference.StoragePath);
        Assert.Equal("sha256:abc", reference.ContentHash);
    }

    [Fact]
    public void EqualityIsByValue()
    {
        var a = new ProfilePostImageRef("p1", 1, "path", "hash");
        var b = new ProfilePostImageRef("p1", 1, "path", "hash");

        Assert.Equal(a, b);
    }
}
