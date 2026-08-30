using Incoders.Hive.Domain.Social.ContentGeneration;
using System.Security.Cryptography;

namespace Incoders.Hive.Tests.Domain.Social.ContentGeneration;

/// <summary>
/// TDD tests for <see cref="RenderedAsset"/> invariants.
/// </summary>
public sealed class RenderedAssetTests
{
    [Fact]
    public void Create_WithValidPngBytes_ProducesAsset()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        var asset = RenderedAsset.Create("carousel", "image/png", "carousel.png", bytes);

        Assert.Equal("carousel", asset.FormatKey);
        Assert.Equal("image/png", asset.Mime);
        Assert.Equal("carousel.png", asset.Filename);
        Assert.Equal(bytes.Length, asset.SizeBytes);
    }

    [Fact]
    public void ContentHash_MatchesSha256OfBytes()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var asset = RenderedAsset.Create("hook", "image/png", "hook.png", bytes);

        var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Equal(expected, asset.ContentHash);
    }

    [Fact]
    public void SizeBytes_MatchesBytesLength()
    {
        var bytes = new byte[256];
        Random.Shared.NextBytes(bytes);
        var asset = RenderedAsset.Create("caption", "image/png", "caption.png", bytes);

        Assert.Equal(256, asset.SizeBytes);
    }

    [Fact]
    public void GetBytes_ReturnsOriginalBytes()
    {
        var bytes = new byte[] { 10, 20, 30, 40 };
        var asset = RenderedAsset.Create("story", "image/png", "story.png", bytes);

        Assert.Equal(bytes, asset.GetBytes());
    }

    [Fact]
    public void Create_EmptyMime_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RenderedAsset.Create("carousel", "", "carousel.png", new byte[] { 1 }));
    }

    [Fact]
    public void Create_EmptyFilename_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RenderedAsset.Create("carousel", "image/png", "", new byte[] { 1 }));
    }

    [Fact]
    public void Create_EmptyBytes_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RenderedAsset.Create("carousel", "image/png", "carousel.png", Array.Empty<byte>()));
    }
}
