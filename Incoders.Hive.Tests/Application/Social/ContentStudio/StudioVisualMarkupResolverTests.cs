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
    public async Task ResolveBackgroundRefsAsync_MarkupImgSrcStoragePath_DiffersFromBackgroundImageRef_InlinesImgSrc()
    {
        // Real production shape (job 8d03e5e3): the persisted markup embeds the AI background as a
        // raw storage-path <img src>, while the separate backgroundImageRef field points at a
        // DIFFERENT storage path (a different format result). The browser renders the markup, so the
        // <img src> is what must be inlined — resolving only backgroundImageRef leaves the image
        // unfetchable and the studio shows no background.
        const string imgSrcPath = "org/run/fmtA/carousel/slide_1.png";
        const string bgRefPath = "org/run/fmtB/feed-carousel/slide_1_bg.png";
        var markup =
            "{\"schemaVersion\":1,\"markup\":\"<img src=\\\"" + imgSrcPath + "\\\" alt=\\\"ai-generated-background\\\" />\",\"canvas\":{\"width\":1080,\"height\":1080,\"dpi\":96},\"slotGeometry\":[],\"backgroundImageRef\":\"" + bgRefPath + "\"}";

        var storage = new FakeGeneratedAssetStorage(imgSrcPath, new byte[] { 9, 8, 7 }, "image/png");

        var resolved = await StudioVisualMarkupResolver.ResolveBackgroundRefsAsync(markup, storage, CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Contains("data:image/png;base64,", resolved);
        Assert.DoesNotContain(imgSrcPath, resolved);
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

    // -----------------------------------------------------------------------
    // CAGH-SBR-01 (client-aesthetic-generation-hardening, WU-7c): confirms F2/design.md's finding
    // that this resolver needs NO code change to cover the icon-mark's brand-mark crop ref — the
    // generic SrcAttribute regex above already inlines EVERY src="..." in the markup, not only the
    // background. This is a REGRESSION guard against that finding silently going stale.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ResolveBackgroundRefsAsync_BrandMarkImgSrcStoragePath_ResolvesUnmodified_CAGH_SBR_01_a()
    {
        const string brandMarkStoragePath = "org/client/analysis/brand-mark.png";
        var markup =
            "{\"schemaVersion\":1,\"markup\":\"<div data-layer=\\\"content\\\">" +
            "<h1 data-slot=\\\"headline\\\">Hi</h1>" +
            "<img class=\\\"slot\\\" data-slot=\\\"brand-mark\\\" src=\\\"" + brandMarkStoragePath + "\\\" />" +
            "</div>\",\"canvas\":{\"width\":1080,\"height\":1080,\"dpi\":96},\"slotGeometry\":[]}";

        var storage = new FakeGeneratedAssetStorage(brandMarkStoragePath, new byte[] { 4, 5, 6 }, "image/png");

        var resolved = await StudioVisualMarkupResolver.ResolveBackgroundRefsAsync(markup, storage, CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Contains("data:image/png;base64,", resolved);
        Assert.DoesNotContain(brandMarkStoragePath, resolved);
        // The unrelated headline slot is otherwise unaffected (still present, whatever its
        // re-serialized escaping looks like).
        Assert.Contains("headline", resolved);
    }

    [Fact]
    public async Task ResolveBackgroundRefsAsync_NoBrandMarkRef_UnaffectedByBrandMarkResolution_CAGH_SBR_01_b()
    {
        // Text mark / chip fallback / no mark: no brand-mark <img>, resolution behaves exactly as
        // before this change — already proven generically by ResolveBackgroundRefsAsync_NoBackgroundRef
        // above; this asserts the same for a slide whose ONLY slot content is a non-img brand-mark
        // chip (a <div data-slot="brand-mark">, never an <img>).
        var markup =
            "{\"schemaVersion\":1,\"markup\":\"<div data-layer=\\\"content\\\">" +
            "<div class=\\\"slot\\\" data-slot=\\\"brand-mark\\\" style=\\\"background:#445566;\\\"></div>" +
            "</div>\",\"canvas\":{\"width\":1080,\"height\":1080,\"dpi\":96},\"slotGeometry\":[]}";

        var storage = new FakeGeneratedAssetStorage("unused", Array.Empty<byte>(), "image/png");

        var resolved = await StudioVisualMarkupResolver.ResolveBackgroundRefsAsync(markup, storage, CancellationToken.None);

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
