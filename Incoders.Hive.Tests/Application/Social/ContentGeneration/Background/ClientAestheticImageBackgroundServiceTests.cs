using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Microsoft.Extensions.Logging.Abstractions;
// WU-9: alias removed — IImageGenerationClient retired; ImageGenerationRequest is unambiguous now.

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// WU-4 RED tests: ClientAestheticImageBackgroundService — cap + bounded parallel +
/// reuse-by-prompt-hash + per-slide fallback on prompt/image/storage failure.
/// Refs: IMG-03, IMG-04, IMG-05, CIG-CG-03, CIG-CG-04; design §3.3, §4.1, §5, OQ-3, OQ-4; tasks 4.1.
/// </summary>
public sealed class ClientAestheticImageBackgroundServiceTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid FormatResultId = Guid.NewGuid();

    private static ClientAestheticProfile RichProfile() => new(
        BackgroundDescriptors: ["dark botanical"],
        VoiceTone: "warm",
        AverageSlidesPerCarousel: 5.0,
        AnalyzedAtUtc: DateTime.UtcNow,
        StyleDescriptors: ["organic"],
        Motifs: ["leaves"],
        CaptionVoice: "friendly",
        Palette: [new AestheticPaletteEntry("#3D2B1F", "primary", 0)]);

    private static ClientAestheticImageRequest BuildRequest(
        IReadOnlyList<int> creativeSlideIndices,
        int slideCount = 5,
        IReadOnlyList<string>? perSlideCopySummaries = null)
    {
        perSlideCopySummaries ??= Enumerable.Range(1, slideCount).Select(i => $"Slide {i} copy").ToList();
        return new ClientAestheticImageRequest(
            OrganizationId: OrgId,
            ClientId: ClientId,
            JobId: JobId,
            FormatResultId: FormatResultId,
            FormatKey: "carousel-4",
            SlideCount: slideCount,
            CreativeSlideIndices: creativeSlideIndices,
            Aesthetic: RichProfile(),
            PerSlideCopySummaries: perSlideCopySummaries);
    }

    // -----------------------------------------------------------------------
    // Happy path: both slides succeed → map contains both ForcedDefault entries
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgroundsAsync_HappyPath_ReturnsBothSlides()
    {
        var promptWriter = new StaticPromptWriter(prompt: "a beautiful background");
        var imageGateway = new StaticImageGateway(bytes: [1, 2, 3], mime: "image/png");
        var storage = new CapturingStorage(url: $"orgs/{OrgId}/{FormatResultId}/carousel/slide_{{0}}.png");

        var sut = BuildSut(promptWriter, imageGateway, storage);

        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([1, 4]), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(1, result.Keys);
        Assert.Contains(4, result.Keys);

        var fd1 = result[1];
        var fd4 = result[4];

        Assert.Equal("ai-generated-background", fd1.Name);
        Assert.Equal("ai-generated-background", fd4.Name);
        Assert.False(string.IsNullOrEmpty(fd1.Url));
        Assert.False(string.IsNullOrEmpty(fd4.Url));
    }

    // -----------------------------------------------------------------------
    // Cap enforcement (OQ-2): 3 indices → only first 2 processed (capped to MaxCreativeSlidesPerPackage=2)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgroundsAsync_CapEnforcement_OnlyProcessesFirstTwo()
    {
        var callTracker = new List<int>();
        var promptWriter = new TrackingPromptWriter(callTracker, prompt: "background prompt");
        var imageGateway = new StaticImageGateway(bytes: [1, 2, 3], mime: "image/png");
        var storage = new CapturingStorage(url: "https://example.com/image.png");

        var sut = BuildSut(promptWriter, imageGateway, storage);

        // 3 indices supplied — only first 2 should be processed (cap=2)
        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([1, 2, 3]), CancellationToken.None);

        // Slide 3 must NOT be in the result
        Assert.DoesNotContain(3, result.Keys);
        // First 2 must be present
        Assert.Contains(1, result.Keys);
        Assert.Contains(2, result.Keys);
        // Prompt writer called exactly 2 times
        Assert.Equal(2, callTracker.Count);
        Assert.DoesNotContain(3, callTracker);
    }

    // -----------------------------------------------------------------------
    // Per-slide fallback on prompt failure (IMG-03-b / CIG-CG-03)
    // Slide 1 prompt fails → slide 1 absent; slide 4 proceeds and succeeds.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgroundsAsync_PromptFailureSlide1_Slide1AbsentSlide4Succeeds()
    {
        // Fail slide 1, succeed slide 4
        var promptWriter = new PerSlidePromptWriter(failures: [1], successPrompt: "background");
        var imageGateway = new StaticImageGateway(bytes: [1, 2, 3], mime: "image/png");
        var storage = new CapturingStorage(url: "https://example.com/image.png");

        var sut = BuildSut(promptWriter, imageGateway, storage);

        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([1, 4]), CancellationToken.None);

        Assert.DoesNotContain(1, result.Keys);
        Assert.Contains(4, result.Keys);
    }

    // -----------------------------------------------------------------------
    // Per-slide fallback on image-gen failure (IMG-05-a / CIG-CG-03)
    // Image gateway fails for slide 4 → slide 4 absent; slide 1 succeeds.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgroundsAsync_ImageGenFailureSlide4_Slide4AbsentSlide1Succeeds()
    {
        var promptWriter = new StaticPromptWriter(prompt: "background prompt");
        // For request [1, 4]: slide 1 is 1st call, slide 4 is 2nd call → fail on call 2
        var imageGateway = new PerSlideImageGateway(failOnCallNumbers: [2], bytes: [1, 2, 3], mime: "image/png");
        var storage = new CapturingStorage(url: "https://example.com/image.png");

        var sut = BuildSut(promptWriter, imageGateway, storage);

        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([1, 4]), CancellationToken.None);

        Assert.DoesNotContain(4, result.Keys);
        Assert.Contains(1, result.Keys);
    }

    // -----------------------------------------------------------------------
    // Per-slide fallback on storage failure (IMG-04-b / CIG-CG-03)
    // PutAssetAsync throws for slide 1 → slide 1 absent; slide 4 not affected.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgroundsAsync_StorageFailureSlide1_Slide1AbsentSlide4Succeeds()
    {
        var promptWriter = new StaticPromptWriter(prompt: "background prompt");
        var imageGateway = new StaticImageGateway(bytes: [1, 2, 3], mime: "image/png");
        var storage = new ThrowingStorage(throwForRelativePaths: ["carousel/slide_1.png"]);

        var sut = BuildSut(promptWriter, imageGateway, storage);

        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([1, 4]), CancellationToken.None);

        Assert.DoesNotContain(1, result.Keys);
        Assert.Contains(4, result.Keys);
    }

    // -----------------------------------------------------------------------
    // Reuse on unchanged prompt hash (OQ-3)
    // Asset already exists at the key AND prompt hash unchanged → PutAssetAsync NOT called again;
    // stored URL is reused.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgroundsAsync_PromptHashUnchanged_ReuseStoredUrl()
    {
        var prompt = "stable background prompt";
        var promptWriter = new StaticPromptWriter(prompt: prompt);
        var imageGateway = new StaticImageGateway(bytes: [1, 2, 3], mime: "image/png");

        // Pre-seed: asset exists for slide 1 with the same prompt hash
        var existingUrl = "https://cdn.example.com/existing/slide_1.png";
        var promptHash = ClientAestheticImageBackgroundService.ComputePromptHash(prompt);
        var storage = new HashAwareStorage(
            existingAssets: new Dictionary<string, (string url, string hash)>
            {
                ["carousel/slide_1.png"] = (existingUrl, promptHash)
            },
            newUrl: "https://cdn.example.com/new/slide_1.png");

        var sut = BuildSut(promptWriter, imageGateway, storage);

        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([1]), CancellationToken.None);

        // Must contain slide 1 with the EXISTING url (reused)
        Assert.Contains(1, result.Keys);
        Assert.Equal(existingUrl, result[1].Url);
        // PutAssetAsync must NOT have been called
        Assert.Equal(0, storage.PutCallCount);
        // Image gateway must NOT have been called
        Assert.Equal(0, imageGateway.CallCount);
    }

    // -----------------------------------------------------------------------
    // Regenerate on prompt hash change (OQ-3)
    // Stored asset exists BUT prompt hash differs → image gateway IS called, PutAssetAsync IS called.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgroundsAsync_PromptHashChanged_Regenerates()
    {
        var newPrompt = "new background prompt — style changed";
        var promptWriter = new StaticPromptWriter(prompt: newPrompt);
        var imageGateway = new StaticImageGateway(bytes: [4, 5, 6], mime: "image/png");

        var oldHash = "old-hash-does-not-match";
        var storage = new HashAwareStorage(
            existingAssets: new Dictionary<string, (string url, string hash)>
            {
                ["carousel/slide_1.png"] = ("https://cdn.example.com/old/slide_1.png", oldHash)
            },
            newUrl: "https://cdn.example.com/new/slide_1.png");

        var sut = BuildSut(promptWriter, imageGateway, storage);

        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([1]), CancellationToken.None);

        // Slide 1 must be in the result
        Assert.Contains(1, result.Keys);
        // Image gateway MUST have been called (regeneration)
        Assert.Equal(1, imageGateway.CallCount);
        // Storage PUT MUST have been called at least once (image + sidecar)
        Assert.True(storage.PutCallCount >= 1, $"Expected at least 1 PUT call, got {storage.PutCallCount}");
        // URL should be the new one
        Assert.Equal("https://cdn.example.com/new/slide_1.png", result[1].Url);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ClientAestheticImageBackgroundService BuildSut(
        IImagePromptWriter promptWriter,
        IImageGenerationGateway imageGateway,
        IGeneratedAssetStorage storage)
    {
        return new ClientAestheticImageBackgroundService(
            promptWriter,
            imageGateway,
            storage,
            NullLogger<ClientAestheticImageBackgroundService>.Instance);
    }
}

// -----------------------------------------------------------------------
// Test doubles
// -----------------------------------------------------------------------

file sealed class StaticPromptWriter : IImagePromptWriter
{
    private readonly string _prompt;
    public StaticPromptWriter(string prompt) => _prompt = prompt;

    public Task<Result<string>> WriteAsync(ImagePromptRequest request, CancellationToken ct = default)
        => Task.FromResult(Result.Success(_prompt));
}

file sealed class TrackingPromptWriter : IImagePromptWriter
{
    private readonly List<int> _tracker;
    private readonly string _prompt;

    public TrackingPromptWriter(List<int> tracker, string prompt)
    {
        _tracker = tracker;
        _prompt = prompt;
    }

    public Task<Result<string>> WriteAsync(ImagePromptRequest request, CancellationToken ct = default)
    {
        _tracker.Add(request.SlideIndex);
        return Task.FromResult(Result.Success(_prompt));
    }
}

file sealed class PerSlidePromptWriter : IImagePromptWriter
{
    private readonly HashSet<int> _failures;
    private readonly string _successPrompt;

    public PerSlidePromptWriter(IEnumerable<int> failures, string successPrompt)
    {
        _failures = new HashSet<int>(failures);
        _successPrompt = successPrompt;
    }

    public Task<Result<string>> WriteAsync(ImagePromptRequest request, CancellationToken ct = default)
    {
        if (_failures.Contains(request.SlideIndex))
        {
            return Task.FromResult(Result.Failure<string>(
                Error.Failure("image-prompt.failed", "prompt generation failed")));
        }
        return Task.FromResult(Result.Success(_successPrompt));
    }
}

file sealed class StaticImageGateway : IImageGenerationGateway
{
    private readonly byte[] _bytes;
    private readonly string _mime;
    private int _callCount;

    public int CallCount => _callCount;

    public StaticImageGateway(byte[] bytes, string mime)
    {
        _bytes = bytes;
        _mime = mime;
    }

    public Task<Result<ImageGenerationResult>> GenerateAsync(ImageGenerationRequest request, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _callCount);
        var result = new ImageGenerationResult(_bytes, _mime, "openai", "dall-e-3");
        return Task.FromResult(Result.Success(result));
    }
}

/// <summary>
/// Image gateway that fails on the N-th call (1-based). Used to simulate per-slide failure
/// when we know the call order (slide 1 = first call, slide 4 = second call for [1,4]).
/// </summary>
file sealed class PerSlideImageGateway : IImageGenerationGateway
{
    private readonly HashSet<int> _failOnCallNumber; // 1-based call number to fail on
    private readonly byte[] _bytes;
    private readonly string _mime;
    private int _callCount;

    public PerSlideImageGateway(IEnumerable<int> failOnCallNumbers, byte[] bytes, string mime)
    {
        _failOnCallNumber = new HashSet<int>(failOnCallNumbers);
        _bytes = bytes;
        _mime = mime;
    }

    public Task<Result<ImageGenerationResult>> GenerateAsync(ImageGenerationRequest request, CancellationToken ct = default)
    {
        var callNum = Interlocked.Increment(ref _callCount);
        if (_failOnCallNumber.Contains(callNum))
        {
            return Task.FromResult(Result.Failure<ImageGenerationResult>(
                Error.Failure("image-gen.failed", $"image generation failed on call {callNum}")));
        }

        return Task.FromResult(Result.Success(new ImageGenerationResult(_bytes, _mime, "openai", "dall-e-3")));
    }
}

file sealed class CapturingStorage : IGeneratedAssetStorage
{
    private readonly string _urlTemplate;

    public CapturingStorage(string url) => _urlTemplate = url;

    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
    {
        var url = _urlTemplate.Contains("{0}")
            ? string.Format(_urlTemplate, key.RelativePath)
            : _urlTemplate;

        return Task.FromResult(new StoredAssetRef(url, content.Mime, content.Bytes.Length, "hash"));
    }

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
        => Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}

file sealed class ThrowingStorage : IGeneratedAssetStorage
{
    private readonly HashSet<string> _throwForRelativePaths;

    public ThrowingStorage(IEnumerable<string> throwForRelativePaths)
    {
        _throwForRelativePaths = new HashSet<string>(throwForRelativePaths);
    }

    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
    {
        if (_throwForRelativePaths.Contains(key.RelativePath))
        {
            throw new GeneratedAssetStorageException($"Simulated PUT failure for '{key.RelativePath}'");
        }
        return Task.FromResult(new StoredAssetRef("https://example.com/ok.png", content.Mime, content.Bytes.Length, "hash"));
    }

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
        => Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Storage double that pre-seeds a hash sidecar file for an existing image asset.
/// The service checks for a companion hash sidecar (carousel/slide_N.png.hash) to
/// decide whether to reuse the stored asset (OQ-3).
/// </summary>
file sealed class HashAwareStorage : IGeneratedAssetStorage
{
    /// <summary>Existing hash sidecars: sidecar relativePath → (existingImageUrl, storedHash).</summary>
    private readonly Dictionary<string, (string imageUrl, string hash)> _sidecars;
    private readonly string _newUrl;
    private int _putCallCount;

    public int PutCallCount => _putCallCount;

    public HashAwareStorage(Dictionary<string, (string url, string hash)> existingAssets, string newUrl)
    {
        // existingAssets key = image relative path (e.g. "carousel/slide_1.png")
        // service looks for sidecar: "carousel/slide_1.png.hash"
        _sidecars = existingAssets.ToDictionary(
            kv => kv.Key + ".hash",
            kv => kv.Value);
        _newUrl = newUrl;
    }

    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
    {
        Interlocked.Increment(ref _putCallCount);
        return Task.FromResult(new StoredAssetRef(_newUrl, content.Mime, content.Bytes.Length, "new-hash"));
    }

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);

    /// <summary>
    /// Returns the sidecar content (hash + imageUrl, pipe-separated) when the sidecar relative
    /// path matches a seeded entry. Returns NotFound otherwise.
    /// StoragePath format (built by service): orgId/clientId/formatResultId/carousel/slide_1.png.hash
    /// We match on the last 2 segments: carousel/slide_1.png.hash
    /// </summary>
    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
    {
        var segments = storagePath.Split('/');
        if (segments.Length >= 2)
        {
            var relativePath = string.Join("/", segments.TakeLast(2));
            if (_sidecars.TryGetValue(relativePath, out var sidecar))
            {
                var content = $"{sidecar.hash}|{sidecar.imageUrl}";
                var bytes = System.Text.Encoding.UTF8.GetBytes(content);
                var stream = new MemoryStream(bytes);
                return Task.FromResult(Result.Success(new StoredAssetStream(stream, "text/plain", bytes.Length)));
            }
        }
        return Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}
