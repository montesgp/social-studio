using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Domain.Social.ContentGeneration;

/// <summary>
/// TDD tests for <see cref="GeneratedPackageManifest"/> construction and content.
/// </summary>
public sealed class GeneratedPackageManifestTests
{
    [Fact]
    public void Create_WithSingleFormat_ManifestHasOneEntry()
    {
        var formats = new Dictionary<string, FormatAssetMetadata>
        {
            ["carousel"] = new FormatAssetMetadata("carousel", "image/png", "carousel.png", 2048, "abc123")
        };

        var manifest = new GeneratedPackageManifest(
            Formats: formats,
            InferredFields: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            TotalSizeBytes: 2048);

        Assert.Single(manifest.Formats);
        Assert.True(manifest.Formats.ContainsKey("carousel"));
    }

    [Fact]
    public void Create_WithMultipleFormats_TotalSizeAggregated()
    {
        var formats = new Dictionary<string, FormatAssetMetadata>
        {
            ["carousel"] = new FormatAssetMetadata("carousel", "image/png", "carousel.png", 1024, "hash1"),
            ["flyer"]    = new FormatAssetMetadata("flyer",    "image/png", "flyer.png",    2048, "hash2")
        };

        var manifest = new GeneratedPackageManifest(
            Formats: formats,
            InferredFields: new[] { "brand.ColorPalette" },
            Warnings: new[] { "carousel: brand color inferred" },
            TotalSizeBytes: 3072);

        Assert.Equal(2, manifest.Formats.Count);
        Assert.Single(manifest.InferredFields);
        Assert.Single(manifest.Warnings);
        Assert.Equal(3072, manifest.TotalSizeBytes);
    }

    [Fact]
    public void FormatAssetMetadata_PropertiesPreserved()
    {
        var meta = new FormatAssetMetadata(
            FormatKey: "hook",
            Mime: "image/png",
            Filename: "hook.png",
            SizeBytes: 512,
            ContentHash: "deadbeef");

        Assert.Equal("hook", meta.FormatKey);
        Assert.Equal("image/png", meta.Mime);
        Assert.Equal("hook.png", meta.Filename);
        Assert.Equal(512, meta.SizeBytes);
        Assert.Equal("deadbeef", meta.ContentHash);
    }
}
