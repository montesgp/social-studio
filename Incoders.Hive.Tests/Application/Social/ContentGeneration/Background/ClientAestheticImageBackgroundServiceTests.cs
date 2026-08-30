using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
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
        IReadOnlyList<string>? perSlideCopySummaries = null,
        string formatKey = "carousel-4")
    {
        perSlideCopySummaries ??= Enumerable.Range(1, slideCount).Select(i => $"Slide {i} copy").ToList();
        return new ClientAestheticImageRequest(
            OrganizationId: OrgId,
            ClientId: ClientId,
            JobId: JobId,
            FormatResultId: FormatResultId,
            FormatKey: formatKey,
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
    // CAC-IMG-04 (pixelation fix): the image request is sized from the format's real canvas,
    // not the old hardcoded 1024x1024 square (job 25a65dac-a31e-4eaf-b9a1-346111f67be7).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgroundsAsync_CarouselFormat_RequestsCanvasSizedImageNotHardcodedSquare()
    {
        var promptWriter = new StaticPromptWriter(prompt: "a beautiful background");
        var imageGateway = new CapturingImageGateway(bytes: [1, 2, 3], mime: "image/png");
        var storage = new CapturingStorage(url: $"orgs/{OrgId}/{FormatResultId}/carousel/slide_{{0}}.png");

        var sut = BuildSut(promptWriter, imageGateway, storage);

        // BuildRequest uses FormatKey "carousel-4" (not a catalog key) in the other tests in this
        // file; this test needs the REAL "carousel" catalog key so CompositionDefaults resolves the
        // actual 1080x1350 canvas instead of falling back to the 1080x1080 default.
        var request = BuildRequest([1], formatKey: "carousel");

        await sut.BuildCreativeBackgroundsAsync(request, CancellationToken.None);

        Assert.NotNull(imageGateway.LastRequest);
        // 1024x1280 is an EXACT 4:5 match for the 1080x1350 carousel canvas (so the render never
        // crops), with both dimensions on a multiple of 64 as the UNet requires. Snapping to a
        // multiple of 8 instead produced 1080x1352, which the diffusion backend could not allocate —
        // see ClientAestheticImageBackgroundServiceDimensionTests for the measured evidence.
        Assert.Equal(1024, imageGateway.LastRequest!.Width);
        Assert.Equal(1280, imageGateway.LastRequest!.Height);
        Assert.Equal(1080d / 1350d, (double)imageGateway.LastRequest!.Width / imageGateway.LastRequest!.Height, precision: 10);
    }

    // -----------------------------------------------------------------------
    // Cap enforcement (OQ-2): 4 indices → only first 3 processed (capped to MaxCreativeSlidesPerPackage=3)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgroundsAsync_CapEnforcement_OnlyProcessesFirstThree()
    {
        var callTracker = new List<int>();
        var promptWriter = new TrackingPromptWriter(callTracker, prompt: "background prompt");
        var imageGateway = new StaticImageGateway(bytes: [1, 2, 3], mime: "image/png");
        var storage = new CapturingStorage(url: "https://example.com/image.png");

        var sut = BuildSut(promptWriter, imageGateway, storage);

        // 4 indices supplied — only first 3 should be processed (cap=3)
        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([1, 2, 3, 4]), CancellationToken.None);

        // Slide 4 must NOT be in the result
        Assert.DoesNotContain(4, result.Keys);
        // First 3 must be present
        Assert.Contains(1, result.Keys);
        Assert.Contains(2, result.Keys);
        Assert.Contains(3, result.Keys);
        // Prompt writer called exactly 3 times
        Assert.Equal(3, callTracker.Count);
        Assert.DoesNotContain(4, callTracker);
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
    // FIX-5: read and write paths use the same storage key derivation
    // If the service reads from a path derived from hashKey fields, it must
    // match exactly what PersistHashSidecarAsync writes — divergence would
    // cause reuse-by-hash to always miss even when the prompt is unchanged.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TryReuseAsync_ReadAndWritePaths_DeriveFromSameKey()
    {
        // GIVEN: a storage that records every path opened.
        var trackingStorage = new PathTrackingStorage();

        // The write path (PersistHashSidecarAsync) builds:
        //   {org}/{client}/{formatResultId}/{hashRelativePath}
        // which for slide 1 is:
        //   {OrgId}/{ClientId}/{FormatResultId}/carousel/slide_1.png.hash
        var expectedSidecarPath = $"{OrgId}/{ClientId}/{FormatResultId}/carousel/slide_1.png.hash";

        // Seed the sidecar at the WRITE path so the read path will find it when it opens the same path.
        const string reuseUrl = "https://cdn.example.com/slide_1.png";
        var promptText = "stable background for reuse test";
        var realHash = ClientAestheticImageBackgroundService.ComputePromptHash(promptText);
        trackingStorage.SeedSidecar(expectedSidecarPath, realHash, reuseUrl);

        var promptWriter = new StaticPromptWriter(prompt: promptText);
        var imageGateway = new StaticImageGateway(bytes: [1, 2, 3], mime: "image/png");
        var sut = BuildSut(promptWriter, imageGateway, trackingStorage);

        // WHEN
        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([1]), CancellationToken.None);

        // THEN: the read path must have opened the EXACT same path as the write path builds,
        // proving there is no read/write path divergence (FIX-5).
        Assert.True(trackingStorage.OpenedPaths.Contains(expectedSidecarPath),
            $"Read path opened '{string.Join(", ", trackingStorage.OpenedPaths)}' " +
            $"but expected '{expectedSidecarPath}'. Read/write path divergence detected (FIX-5).");

        // AND the result should reuse the existing URL (the sidecar hash matched)
        Assert.Contains(1, result.Keys);
        Assert.Equal(reuseUrl, result[1].Url);
    }

    // -----------------------------------------------------------------------
    // Regression (DbContext concurrency incident): the per-slide loop MUST run serially.
    // The defect: BuildCreativeBackgroundsAsync fanned the <=2 creative slides out with
    // Task.WhenAll, and each slide's prompt-write transitively resolves workflow config
    // through a scoped EF DbContext (ImagePromptWriter -> WorkflowConfigurationResolver ->
    // EfWorkflowStepConfigurationRepository). EF Core's DbContext is not thread-safe, so the
    // parallel slides raced the shared context ("A second operation was started on this context
    // instance before a previous operation completed") and the losing slide degraded to CSS —
    // the carousel came out without its generated photo slides.
    // This fake throws exactly like EF on concurrent entry; a serialized loop never triggers it.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgroundsAsync_PerSlideLoop_RunsSeriallyWithoutConcurrentDbAccess()
    {
        var promptWriter = new ConcurrencyGuardPromptWriter(prompt: "background prompt");
        var imageGateway = new StaticImageGateway(bytes: [1, 2, 3], mime: "image/png");
        var storage = new CapturingStorage(url: "https://example.com/image.png");

        var sut = BuildSut(promptWriter, imageGateway, storage);

        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([1, 4]), CancellationToken.None);

        // If the loop ran in parallel, the two prompt-writes would overlap and the guard would
        // observe concurrent access — exactly as the real shared DbContext did in production.
        Assert.False(promptWriter.SawConcurrentAccess,
            "Creative-slide prompt-writes ran concurrently. They share a scoped EF DbContext and " +
            "MUST be serialized (regression: 'A second operation was started on this context instance').");

        // And with serial execution both creative slides must succeed (neither degrades to CSS).
        Assert.Equal(2, result.Count);
        Assert.Contains(1, result.Keys);
        Assert.Contains(4, result.Keys);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // A cancelled job must stop the slide walk, not degrade slide by slide. Degrading is the right
    // answer when a slide FAILS — the rest of the carousel is still wanted. It is the wrong answer
    // when the job itself is over: every remaining slide then buys a fresh round of provider
    // attempts against a budget nobody is waiting on.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgroundsAsync_JobCancelledDuringSlide1_DoesNotStartSlide4()
    {
        var callTracker = new List<int>();
        var promptWriter = new TrackingPromptWriter(callTracker, prompt: "background prompt");
        using var cts = new CancellationTokenSource();
        var imageGateway = new CancellingImageGateway(cts);
        var storage = new CapturingStorage(url: "https://example.com/image.png");

        var sut = BuildSut(promptWriter, imageGateway, storage);

        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([1, 4]), cts.Token);

        Assert.Equal([1], callTracker);
        Assert.DoesNotContain(4, result.Keys);
    }

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

/// <summary>
/// Simulates the non-thread-safe EF DbContext used by ImagePromptWriter's workflow-config
/// resolution: if two prompt-writes overlap, it records the violation and throws the exact
/// "second operation on this context instance" InvalidOperationException that EF Core raises.
/// A serialized per-slide loop keeps in-flight count at 1 and never triggers it.
/// The Task.Delay widens the overlap window so a parallel loop collides deterministically.
/// </summary>
file sealed class ConcurrencyGuardPromptWriter : IImagePromptWriter
{
    private readonly string _prompt;
    private int _active;
    private volatile bool _sawConcurrentAccess;

    public ConcurrencyGuardPromptWriter(string prompt) => _prompt = prompt;

    public bool SawConcurrentAccess => _sawConcurrentAccess;

    public async Task<Result<string>> WriteAsync(ImagePromptRequest request, CancellationToken ct = default)
    {
        if (Interlocked.Increment(ref _active) > 1)
        {
            _sawConcurrentAccess = true;
            Interlocked.Decrement(ref _active);
            throw new InvalidOperationException(
                "A second operation was started on this context instance before a previous " +
                "operation completed. This is usually caused by different threads concurrently " +
                "using the same instance of DbContext.");
        }

        try
        {
            await Task.Delay(50, ct);
            return Result.Success(_prompt);
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }
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

    public Task<Result<ImageModelCapabilities>> DescribeAsync(Guid organizationId, CancellationToken ct = default)
        => Task.FromResult(Result.Success(new ImageModelCapabilities(false, 0)));
}

/// <summary>
/// Image gateway that records the last <see cref="ImageGenerationRequest"/> it received, so a test
/// can assert the resolved Width/Height reached the gateway call (CAC-IMG-04 pixelation fix).
/// </summary>
file sealed class CapturingImageGateway : IImageGenerationGateway
{
    private readonly byte[] _bytes;
    private readonly string _mime;

    public ImageGenerationRequest? LastRequest { get; private set; }

    public CapturingImageGateway(byte[] bytes, string mime)
    {
        _bytes = bytes;
        _mime = mime;
    }

    public Task<Result<ImageGenerationResult>> GenerateAsync(ImageGenerationRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        var result = new ImageGenerationResult(_bytes, _mime, "openai", "dall-e-3");
        return Task.FromResult(Result.Success(result));
    }

    public Task<Result<ImageModelCapabilities>> DescribeAsync(Guid organizationId, CancellationToken ct = default)
        => Task.FromResult(Result.Success(new ImageModelCapabilities(false, 0)));
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

    public Task<Result<ImageModelCapabilities>> DescribeAsync(Guid organizationId, CancellationToken ct = default)
        => Task.FromResult(Result.Success(new ImageModelCapabilities(false, 0)));
}

/// <summary>
/// Stands in for the job budget expiring mid-render: the token is cancelled while the provider call
/// is in flight, and the call reports the cancellation the way the adapters do — as a failed
/// <c>Result</c>, not a thrown exception.
/// </summary>
file sealed class CancellingImageGateway : IImageGenerationGateway
{
    private readonly CancellationTokenSource _cts;

    public CancellingImageGateway(CancellationTokenSource cts) => _cts = cts;

    public Task<Result<ImageGenerationResult>> GenerateAsync(ImageGenerationRequest request, CancellationToken ct = default)
    {
        _cts.Cancel();
        return Task.FromResult(Result.Failure<ImageGenerationResult>(IntegrationErrors.OperationCancelled));
    }

    public Task<Result<ImageModelCapabilities>> DescribeAsync(Guid organizationId, CancellationToken ct = default)
        => Task.FromResult(Result.Success(new ImageModelCapabilities(false, 0)));
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

/// <summary>
/// FIX-5: records every path passed to OpenAsync so tests can assert that the read path
/// derives the storage location from the same source as the write path (PersistHashSidecarAsync).
/// </summary>
file sealed class PathTrackingStorage : IGeneratedAssetStorage
{
    private readonly HashSet<string> _openedPaths = new();
    private readonly Dictionary<string, string> _sidecars = new(); // full path → "hash|imageUrl"
    private string _newUrl = string.Empty;

    public IReadOnlySet<string> OpenedPaths => _openedPaths;

    /// <summary>Seeds a hash sidecar at the given full storage path (orgId/clientId/formatResultId/relative.hash).</summary>
    public void SeedSidecar(string fullPath, string hash, string imageUrl)
    {
        _sidecars[fullPath] = $"{hash}|{imageUrl}";
        _newUrl = imageUrl;
    }

    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct) =>
        Task.FromResult(new StoredAssetRef(_newUrl, content.Mime, content.Bytes.Length, "hash"));

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);

    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
    {
        _openedPaths.Add(storagePath);
        if (_sidecars.TryGetValue(storagePath, out var content))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            return Task.FromResult(Result.Success(new StoredAssetStream(new MemoryStream(bytes), "text/plain", bytes.Length)));
        }
        return Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}
