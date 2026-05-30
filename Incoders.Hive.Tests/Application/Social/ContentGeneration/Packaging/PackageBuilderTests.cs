using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Packaging;
using Incoders.Hive.Domain.Social.ContentGeneration;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Packaging;

/// <summary>
/// TDD tests for <see cref="GeneratedPackageBuilder"/>.
/// </summary>
public sealed class PackageBuilderTests
{
    [Fact]
    public async Task BuildAsync_SingleAsset_ProducesZipWithAssetAndManifest()
    {
        var builder = new GeneratedPackageBuilder();
        var asset = RenderedAsset.Create("carousel", "image/png", "carousel.png", new byte[] { 1, 2, 3, 4, 5 });
        var input = new BuildPackageInput(
            Assets: [asset],
            InferredFields: [],
            Warnings: [],
            OrganizationId: Guid.NewGuid(),
            JobId: Guid.NewGuid());

        var result = await builder.BuildAsync(input, CancellationToken.None);

        Assert.True(result.SizeBytes > 0);
        Assert.NotNull(result.Manifest);
        Assert.Single(result.Manifest.Formats);
        Assert.Equal(result.SizeBytes, result.Manifest.TotalSizeBytes);

        // Verify ZIP contents
        result.Content.Seek(0, SeekOrigin.Begin);
        using var archive = new ZipArchive(result.Content, ZipArchiveMode.Read, leaveOpen: true);
        Assert.Contains(archive.Entries, e => e.FullName == "manifest.json");
        Assert.Contains(archive.Entries, e => e.FullName.Contains("carousel"));
    }

    [Fact]
    public async Task BuildAsync_MultipleAssets_ManifestContainsAllFormats()
    {
        var builder = new GeneratedPackageBuilder();
        var assets = new[]
        {
            RenderedAsset.Create("carousel", "image/png", "carousel.png", new byte[] { 1, 2, 3 }),
            RenderedAsset.Create("flyer", "image/png", "flyer.png", new byte[] { 4, 5, 6, 7 }),
        };
        var input = new BuildPackageInput(
            Assets: assets,
            InferredFields: ["brand.ColorPalette"],
            Warnings: ["carousel: brand color inferred"],
            OrganizationId: Guid.NewGuid(),
            JobId: Guid.NewGuid(),
            PublicationByFormat: new Dictionary<string, PublicationMetadata>
            {
                ["carousel"] = new("Carousel publication description"),
                ["flyer"] = new("Flyer publication description"),
            });

        var result = await builder.BuildAsync(input, CancellationToken.None);

        Assert.Equal(2, result.Manifest.Formats.Count);
        Assert.Contains("carousel", result.Manifest.Formats.Keys);
        Assert.Contains("flyer", result.Manifest.Formats.Keys);
        Assert.Single(result.Manifest.InferredFields);
        Assert.Single(result.Manifest.Warnings);
        Assert.Equal(
            "Carousel publication description",
            result.Manifest.Formats["carousel"].Publication?.Description);
        Assert.Equal(
            "Flyer publication description",
            result.Manifest.Formats["flyer"].Publication?.Description);
    }

    [Fact]
    public async Task BuildAsync_CarouselMultiSlide_ZipHasSlideEntries()
    {
        var builder = new GeneratedPackageBuilder();
        var assets = new[]
        {
            RenderedAsset.Create("carousel", "image/png", "carousel-0.png", new byte[] { 1, 2, 3 }),
            RenderedAsset.Create("carousel", "image/png", "carousel-1.png", new byte[] { 4, 5, 6 }),
        };
        var input = new BuildPackageInput(
            Assets: assets,
            InferredFields: [],
            Warnings: [],
            OrganizationId: Guid.NewGuid(),
            JobId: Guid.NewGuid());

        var result = await builder.BuildAsync(input, CancellationToken.None);

        result.Content.Seek(0, SeekOrigin.Begin);
        using var archive = new ZipArchive(result.Content, ZipArchiveMode.Read, leaveOpen: true);
        // Both carousel slides should be present
        Assert.Equal(2, archive.Entries.Count(e => e.FullName.StartsWith("carousel/")));
    }

    [Fact]
    public async Task BuildAsync_ManifestJson_ContainsFormatsAndInferredFields()
    {
        var builder = new GeneratedPackageBuilder();
        var asset = RenderedAsset.Create("hook", "image/png", "hook.png", new byte[] { 9, 8, 7 });
        var input = new BuildPackageInput(
            Assets: [asset],
            InferredFields: ["brand.Typography"],
            Warnings: [],
            OrganizationId: Guid.NewGuid(),
            JobId: Guid.NewGuid());

        var result = await builder.BuildAsync(input, CancellationToken.None);

        result.Content.Seek(0, SeekOrigin.Begin);
        using var archive = new ZipArchive(result.Content, ZipArchiveMode.Read, leaveOpen: true);
        var manifestEntry = archive.GetEntry("manifest.json");
        Assert.NotNull(manifestEntry);

        await using var stream = manifestEntry!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("formats", out _));
    }

    [Fact]
    public async Task BuildAsync_EmptyAssetList_ThrowsDomainError()
    {
        var builder = new GeneratedPackageBuilder();
        var input = new BuildPackageInput(
            Assets: [],
            InferredFields: [],
            Warnings: [],
            OrganizationId: Guid.NewGuid(),
            JobId: Guid.NewGuid());

        await Assert.ThrowsAsync<PackagingException>(() =>
            builder.BuildAsync(input, CancellationToken.None));
    }
}
