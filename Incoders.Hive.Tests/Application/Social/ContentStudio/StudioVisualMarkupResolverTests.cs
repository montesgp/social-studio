using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentStudio.Shared;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Tests.Application.Social.ContentStudio;

/// <summary>
/// Issue #58: the studio/package preview must render the AI-generated slide background again.
/// A storage-path <c>backgroundImageRef</c> (persisted lightweight per BGREF-01) is not directly
/// fetchable by the browser — <see cref="StudioVisualMarkupResolver"/> inlines it as a data-URI
/// ON READ ONLY, reusing the same path-to-bytes resolution as the PPTX export adapter. It never
/// persists the inlined result back to the DB (that would re-introduce the #56 payload bloat).
/// </summary>
public sealed class StudioVisualMarkupResolverTests
{
    [Fact]
    public async Task ResolveBackgroundRefsAsync_StoragePathBackground_InlinesAsDataUri()
    {
        const string storagePath = "org/client/analysis/feed-carousel/slide_1_bg.png";
        var markup =
            "{\"schemaVersion\":1,\"markup\":\"<img src=\\\"" + storagePath + "\\\" />\",\"canvas\":{\"width\":1080,\"height\":1080,\"dpi\":96},\"slotGeometry\":[],\"backgroundImageRef\":\"" + storagePath + "\"}";

        var storage = new FakeGeneratedAssetStorage(storagePath, new byte[] { 1, 2, 3 }, "image/png");

        var resolved = await StudioVisualMarkupResolver.ResolveBackgroundRefsAsync(markup, storage, CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Contains("data:image/png;base64,", resolved);
        Assert.DoesNotContain(storagePath, resolved);
    }

    [Fact]
    public async Task ResolveBackgroundRefsAsync_LegacyDataUriBackground_LeftUnchanged()
    {
        const string dataUri = "data:image/png;base64,AAAA";
        var markup =
            "{\"schemaVersion\":1,\"markup\":\"<img src=\\\"" + dataUri + "\\\" />\",\"canvas\":{\"width\":1080,\"height\":1080,\"dpi\":96},\"slotGeometry\":[],\"backgroundImageRef\":\"" + dataUri + "\"}";

        var storage = new FakeGeneratedAssetStorage("unused", Array.Empty<byte>(), "image/png");

        var resolved = await StudioVisualMarkupResolver.ResolveBackgroundRefsAsync(markup, storage, CancellationToken.None);

        Assert.Equal(markup, resolved);
    }

    [Fact]
    public async Task ResolveBackgroundRefsAsync_MissingFile_LeavesMarkupUnchanged_NoThrow()
    {
        const string storagePath = "org/client/analysis/feed-carousel/slide_2_bg.png";
        var markup =
            "{\"schemaVersion\":1,\"markup\":\"<img src=\\\"" + storagePath + "\\\" />\",\"canvas\":{\"width\":1080,\"height\":1080,\"dpi\":96},\"slotGeometry\":[],\"backgroundImageRef\":\"" + storagePath + "\"}";

        var storage = new FakeGeneratedAssetStorage(storagePath, bytes: null, mime: null); // simulates NotFound

        var exception = await Record.ExceptionAsync(
            async () => await StudioVisualMarkupResolver.ResolveBackgroundRefsAsync(markup, storage, CancellationToken.None));

        var resolved = await StudioVisualMarkupResolver.ResolveBackgroundRefsAsync(markup, storage, CancellationToken.None);

        Assert.Null(exception);
        Assert.Equal(markup, resolved);
    }

    [Fact]
    public async Task ResolveBackgroundRefsAsync_NoBackgroundRef_LeavesMarkupUnchanged()
    {
        var markup =
            "{\"schemaVersion\":1,\"markup\":\"<div>no background</div>\",\"canvas\":{\"width\":1080,\"height\":1080,\"dpi\":96},\"slotGeometry\":[]}";

        var storage = new FakeGeneratedAssetStorage("unused", Array.Empty<byte>(), "image/png");

        var resolved = await StudioVisualMarkupResolver.ResolveBackgroundRefsAsync(markup, storage, CancellationToken.None);

        Assert.Equal(markup, resolved);
    }

    [Fact]
    public async Task ResolveBackgroundRefsAsync_BlankMarkup_ReturnsInputUnchanged()
    {
        var storage = new FakeGeneratedAssetStorage("unused", Array.Empty<byte>(), "image/png");

        var resolved = await StudioVisualMarkupResolver.ResolveBackgroundRefsAsync(null, storage, CancellationToken.None);

        Assert.Null(resolved);
    }
}

file sealed class FakeGeneratedAssetStorage(string expectedPath, byte[]? bytes, string? mime) : IGeneratedAssetStorage
{
    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
    {
        if (!string.Equals(storagePath, expectedPath, StringComparison.Ordinal) || bytes is null)
        {
            return Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
        }

        var stream = new StoredAssetStream(new MemoryStream(bytes), mime ?? "image/png", bytes.Length);
        return Task.FromResult(Result.Success(stream));
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}
