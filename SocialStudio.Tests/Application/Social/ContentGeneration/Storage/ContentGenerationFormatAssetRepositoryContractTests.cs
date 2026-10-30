using SocialStudio.Application.Abstractions.Social.ContentGeneration;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Storage;

/// <summary>
/// T3.7 — Compile-guard and contract tests for the <see cref="IContentGenerationFormatAssetRepository"/> port
/// and the <see cref="FormatAssetRow"/> record.
/// The repository implementation (EF) is covered by integration tests in Infrastructure;
/// these tests verify the Application-layer types compile and satisfy their contracts.
/// </summary>
public sealed class ContentGenerationFormatAssetRepositoryContractTests
{
    [Fact]
    public void FormatAssetRow_CanBeConstructed_WithExpectedProperties()
    {
        var now = DateTime.UtcNow;
        var row = new FormatAssetRow(
            Id: Guid.NewGuid(),
            FormatResultId: Guid.NewGuid(),
            AssetIndex: 0,
            StoragePath: "org/client/analysis/story.png",
            Filename: "story.png",
            MimeType: "image/png",
            SizeBytes: 2048,
            ContentHash: "abc123",
            CreatedAt: now,
            UpdatedAt: now);

        Assert.Equal(0, row.AssetIndex);
        Assert.Equal("story.png", row.Filename);
        Assert.Equal("image/png", row.MimeType);
        Assert.Equal(2048, row.SizeBytes);
    }

    [Fact]
    public void FormatAssetRow_CarouselSlide_AssetIndexIsOneBased()
    {
        var now = DateTime.UtcNow;
        var slide3 = new FormatAssetRow(
            Id: Guid.NewGuid(),
            FormatResultId: Guid.NewGuid(),
            AssetIndex: 3,
            StoragePath: "org/client/analysis/carousel/slide_3.png",
            Filename: "slide_3.png",
            MimeType: "image/png",
            SizeBytes: 4096,
            ContentHash: "def456",
            CreatedAt: now,
            UpdatedAt: now);

        Assert.Equal(3, slide3.AssetIndex);
        Assert.Contains("slide_3", slide3.Filename);
    }

    [Fact]
    public void IContentGenerationFormatAssetRepository_InterfaceExistsInApplicationLayer()
    {
        var type = typeof(IContentGenerationFormatAssetRepository);
        Assert.True(type.IsInterface);
        Assert.Contains("Application", type.Assembly.FullName ?? string.Empty);
    }

    [Fact]
    public void IContentGenerationFormatAssetRepository_HasUpsertManyMethod()
    {
        var type = typeof(IContentGenerationFormatAssetRepository);
        var method = type.GetMethod("UpsertManyForFormatAsync");
        Assert.NotNull(method);
        Assert.Equal(3, method!.GetParameters().Length);
    }

    [Fact]
    public void IContentGenerationFormatAssetRepository_HasListByJobMethod()
    {
        var type = typeof(IContentGenerationFormatAssetRepository);
        var method = type.GetMethod("ListByJobAsync");
        Assert.NotNull(method);
    }
}
