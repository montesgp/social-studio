using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Pipeline;

/// <summary>
/// studio-background-asset-ref Slice 1, Work Unit 1 (TDD RED before implementation) — the
/// data-URI-to-bytes decoder used to upload the captured background PNG via
/// <c>IGeneratedAssetStorage</c> instead of persisting it as base64 inline in
/// <c>composition_spec_json</c> (BGREF-01).
/// </summary>
public sealed class BackgroundAssetUploadTests
{
    [Fact]
    public void TryDecodeBackgroundDataUri_ValidPngDataUri_DecodesBytesAndMime()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var dataUri = $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";

        var decoded = ContentGenerationJobProcessor.TryDecodeBackgroundDataUri(dataUri);

        Assert.NotNull(decoded);
        Assert.Equal(pngBytes, decoded!.Value.Bytes);
        Assert.Equal("image/png", decoded.Value.Mime);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryDecodeBackgroundDataUri_BlankInput_ReturnsNull(string? input)
    {
        var decoded = ContentGenerationJobProcessor.TryDecodeBackgroundDataUri(input);

        Assert.Null(decoded);
    }

    [Fact]
    public void TryDecodeBackgroundDataUri_NotADataUri_ReturnsNull()
    {
        var decoded = ContentGenerationJobProcessor.TryDecodeBackgroundDataUri(
            "https://cdn.example.com/bg.png");

        Assert.Null(decoded);
    }

    [Fact]
    public void TryDecodeBackgroundDataUri_StoragePath_ReturnsNull()
    {
        var decoded = ContentGenerationJobProcessor.TryDecodeBackgroundDataUri(
            "org/client/analysis/carousel/slide_1_bg.png");

        Assert.Null(decoded);
    }

    [Fact]
    public void TryDecodeBackgroundDataUri_MalformedBase64_ReturnsNull()
    {
        var decoded = ContentGenerationJobProcessor.TryDecodeBackgroundDataUri(
            "data:image/png;base64,not-valid-base64!!!");

        Assert.Null(decoded);
    }
}
