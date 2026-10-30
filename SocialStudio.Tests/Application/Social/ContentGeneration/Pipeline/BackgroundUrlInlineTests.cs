using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Pipeline;
using SocialStudio.Domain.Common;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Pipeline;

/// <summary>
/// Regression for the black Photo-slide bug: hard-enforced background images were referenced in the
/// render HTML by their raw Supabase storage path. The Chromium renderer sets page content with no
/// base URL, so that path could not be fetched and rasterized to a black broken-image background.
/// <see cref="ContentGenerationJobProcessor.InlineBackgroundUrlAsync"/> inlines a bare storage path
/// as a self-contained data URI so the enforced background actually renders.
/// </summary>
public sealed class BackgroundUrlInlineTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];

    [Fact]
    public async Task StoragePath_IsInlinedAsDataUri()
    {
        var storage = new StubStorage(PngBytes, "image/png");

        var result = await ContentGenerationJobProcessor.InlineBackgroundUrlAsync(
            storage, "org/client/analysis/carousel/slide_1.png", CancellationToken.None);

        Assert.StartsWith("data:image/png;base64,", result);
        var base64 = result["data:image/png;base64,".Length..];
        Assert.Equal(PngBytes, Convert.FromBase64String(base64));
        Assert.Equal("org/client/analysis/carousel/slide_1.png", storage.LastOpenedPath);
    }

    [Theory]
    [InlineData("https://cdn.example.com/bg.png")]
    [InlineData("http://cdn.example.com/bg.png")]
    [InlineData("data:image/png;base64,AAAA")]
    public async Task AlreadyLoadableUrl_IsReturnedUnchanged_AndStorageNotTouched(string url)
    {
        var storage = new StubStorage(PngBytes, "image/png");

        var result = await ContentGenerationJobProcessor.InlineBackgroundUrlAsync(
            storage, url, CancellationToken.None);

        Assert.Equal(url, result);
        Assert.Null(storage.LastOpenedPath); // never fetched
    }

    [Fact]
    public async Task NullStorage_ReturnsUrlUnchanged()
    {
        var result = await ContentGenerationJobProcessor.InlineBackgroundUrlAsync(
            storage: null, "org/client/analysis/carousel/slide_1.png", CancellationToken.None);

        Assert.Equal("org/client/analysis/carousel/slide_1.png", result);
    }

    [Fact]
    public async Task StorageReadFailure_ReturnsUrlUnchanged_BestEffort()
    {
        var storage = new StubStorage(bytes: null, mime: "image/png");

        var result = await ContentGenerationJobProcessor.InlineBackgroundUrlAsync(
            storage, "org/client/analysis/carousel/slide_1.png", CancellationToken.None);

        Assert.Equal("org/client/analysis/carousel/slide_1.png", result);
    }

    // Stub: OpenAsync returns the configured bytes (or NotFound when bytes is null). Other members
    // are never exercised by InlineBackgroundUrlAsync.
    private sealed class StubStorage(byte[]? bytes, string mime) : IGeneratedAssetStorage
    {
        public string? LastOpenedPath { get; private set; }

        public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
        {
            LastOpenedPath = storagePath;
            if (bytes is null)
            {
                return Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
            }

            var stream = new StoredAssetStream(new MemoryStream(bytes), mime, bytes.Length);
            return Task.FromResult(Result.Success(stream));
        }

        public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storagePath, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
