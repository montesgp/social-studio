using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// WU-4 RED tests (task 4.10): capability-gated image conditioning in
/// ClientAestheticImageBackgroundService.
///
/// CAC-IMG-01-a: DescribeAsync called ONCE per job; SupportsImageConditioning=true + reference
///   images present → ReferenceImages populated with up to min(MaxConditioningImages,3) Url handles.
/// CAC-IMG-01-b: SupportsImageConditioning=false → ReferenceImages=null; descriptor prompt drives gen.
/// CAC-IMG-01-c: SupportsImageConditioning=true but no reference images on profile → ReferenceImages=null.
/// CAC-IMG-03-c: VisualGuardrailValidator applied per slide + bounded retry + CSS fallback on exhaustion.
///
/// Refs: CAC-IMG-01, CAC-IMG-03; design §3.4, §4.2; tasks 4.10.
/// </summary>
public sealed class ClientAestheticImageBackgroundServiceConditioningTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid FormatResultId = Guid.NewGuid();

    // -----------------------------------------------------------------------
    // CAC-IMG-01: DescribeAsync called exactly once per BuildCreativeBackgroundsAsync call
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgrounds_DescribeAsyncCalledOncePerJob()
    {
        var caps = new ImageModelCapabilities(SupportsImageConditioning: false, MaxConditioningImages: 0);
        var gateway = new ConditioningTrackingGateway(caps);
        var sut = BuildSut(gateway, profile: MinimalProfile());

        await sut.BuildCreativeBackgroundsAsync(
            BuildRequest([1, 4], minimalProfile: true), CancellationToken.None);

        Assert.Equal(1, gateway.DescribeCallCount);
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-01-a: SupportsImageConditioning=true + refs present → ReferenceImages populated
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgrounds_SupportingWithRefs_RequestHasReferenceImages()
    {
        var caps = new ImageModelCapabilities(SupportsImageConditioning: true, MaxConditioningImages: 3);
        var refs = BuildRefImages(count: 5); // 5 refs but capped to min(3,3)=3
        var profile = ProfileWithRefs(refs);
        var gateway = new ConditioningCapturingGateway(caps);
        var sut = BuildSut(gateway, profile: profile);

        await sut.BuildCreativeBackgroundsAsync(
            BuildRequest([1], profile: profile), CancellationToken.None);

        // The captured request must have ReferenceImages
        Assert.NotNull(gateway.CapturedRequest);
        Assert.NotNull(gateway.CapturedRequest!.ReferenceImages);
    }

    [Fact]
    public async Task BuildCreativeBackgrounds_SupportingWithRefs_ReferenceImagesCappedToMin()
    {
        var maxCond = 3;
        var caps = new ImageModelCapabilities(SupportsImageConditioning: true, MaxConditioningImages: maxCond);
        var refs = BuildRefImages(count: 5); // 5 refs, min(MaxCond=3, hardcap=3) = 3
        var profile = ProfileWithRefs(refs);
        var gateway = new ConditioningCapturingGateway(caps);
        var sut = BuildSut(gateway, profile: profile);

        await sut.BuildCreativeBackgroundsAsync(
            BuildRequest([1], profile: profile), CancellationToken.None);

        Assert.NotNull(gateway.CapturedRequest?.ReferenceImages);
        Assert.Equal(3, gateway.CapturedRequest!.ReferenceImages!.Count);
    }

    [Fact]
    public async Task BuildCreativeBackgrounds_SupportingWithRefs_ReferenceImagesAreUrlHandles()
    {
        var caps = new ImageModelCapabilities(SupportsImageConditioning: true, MaxConditioningImages: 3);
        var refs = BuildRefImages(count: 2);
        var profile = ProfileWithRefs(refs);
        var gateway = new ConditioningCapturingGateway(caps);
        var sut = BuildSut(gateway, profile: profile);

        await sut.BuildCreativeBackgroundsAsync(
            BuildRequest([1], profile: profile), CancellationToken.None);

        Assert.NotNull(gateway.CapturedRequest?.ReferenceImages);
        foreach (var refImage in gateway.CapturedRequest!.ReferenceImages!)
        {
            // Application layer passes Url handles, NOT bytes (byte-hauling is the adapter's job)
            Assert.NotNull(refImage.Url);
            Assert.Null(refImage.Bytes); // bytes are null at the Application layer
        }
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-01-b: SupportsImageConditioning=false → ReferenceImages is null
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgrounds_NotSupporting_ReferenceImagesIsNull()
    {
        var caps = new ImageModelCapabilities(SupportsImageConditioning: false, MaxConditioningImages: 0);
        var refs = BuildRefImages(count: 3);
        var profile = ProfileWithRefs(refs);
        var gateway = new ConditioningCapturingGateway(caps);
        var sut = BuildSut(gateway, profile: profile);

        await sut.BuildCreativeBackgroundsAsync(
            BuildRequest([1], profile: profile), CancellationToken.None);

        Assert.NotNull(gateway.CapturedRequest);
        Assert.Null(gateway.CapturedRequest!.ReferenceImages);
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-01-c: SupportsImageConditioning=true but no ref images → ReferenceImages is null
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgrounds_SupportingButNoRefs_ReferenceImagesIsNull()
    {
        var caps = new ImageModelCapabilities(SupportsImageConditioning: true, MaxConditioningImages: 3);
        var profile = MinimalProfile(); // no ReferenceImages
        var gateway = new ConditioningCapturingGateway(caps);
        var sut = BuildSut(gateway, profile: profile);

        await sut.BuildCreativeBackgroundsAsync(
            BuildRequest([1], minimalProfile: true), CancellationToken.None);

        Assert.NotNull(gateway.CapturedRequest);
        Assert.Null(gateway.CapturedRequest!.ReferenceImages);
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-03-c: guardrail exhaustion → slide absent + service returns normally
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgrounds_GuardrailExhausted_SlideAbsentAndServiceCompletesNormally()
    {
        var caps = new ImageModelCapabilities(SupportsImageConditioning: false, MaxConditioningImages: 0);
        var gateway = new ConditioningCapturingGateway(caps);
        var validator = new ConditioningAlwaysFailValidator();
        var sut = BuildSut(gateway, profile: MinimalProfile(), guardrail: validator);

        // Must NOT throw — the service degrades gracefully
        var result = await sut.BuildCreativeBackgroundsAsync(
            BuildRequest([1], minimalProfile: true), CancellationToken.None);

        // Slide 1 should be absent (CSS fallback)
        Assert.DoesNotContain(1, result.Keys);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ClientAestheticImageBackgroundService BuildSut(
        IImageGenerationGateway gateway,
        ClientAestheticProfile profile,
        IImageGuardrailValidator? guardrail = null)
    {
        return new ClientAestheticImageBackgroundService(
            new ConditioningStaticPromptWriter("background prompt"),
            gateway,
            new ConditioningSimpleStorage("https://cdn.example.com/img.png"),
            NullLogger<ClientAestheticImageBackgroundService>.Instance,
            guardrailValidator: guardrail);
    }

    private static ClientAestheticImageRequest BuildRequest(
        IReadOnlyList<int> creativeSlideIndices,
        ClientAestheticProfile? profile = null,
        bool minimalProfile = false)
    {
        var aesthetic = profile ?? MinimalProfile();
        return new ClientAestheticImageRequest(
            OrganizationId: OrgId,
            ClientId: ClientId,
            JobId: JobId,
            FormatResultId: FormatResultId,
            FormatKey: "carousel-4",
            SlideCount: 5,
            CreativeSlideIndices: creativeSlideIndices,
            Aesthetic: aesthetic,
            PerSlideCopySummaries: ["copy 1", "copy 2", "copy 3", "copy 4", "copy 5"]);
    }

    private static ClientAestheticProfile MinimalProfile() =>
        new(
            BackgroundDescriptors: ["botanical"],
            VoiceTone: "warm",
            AverageSlidesPerCarousel: 5.0,
            AnalyzedAtUtc: DateTime.UtcNow);

    private static ClientAestheticProfile ProfileWithRefs(IReadOnlyList<ProfilePostImageRef> refs) =>
        new(
            BackgroundDescriptors: ["botanical"],
            VoiceTone: "warm",
            AverageSlidesPerCarousel: 5.0,
            AnalyzedAtUtc: DateTime.UtcNow,
            ReferenceImages: refs);

    private static IReadOnlyList<ProfilePostImageRef> BuildRefImages(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new ProfilePostImageRef(
                SourcePostId: $"post-{i}",
                AssetIndex: 0,
                StoragePath: $"orgs/{OrgId}/clients/{ClientId}/ref-{i}.jpg",
                ContentHash: $"hash-{i}"))
            .ToList();
}

// -----------------------------------------------------------------------
// Test doubles
// -----------------------------------------------------------------------

/// <summary>Tracks DescribeAsync call count + captures the most recent GenerateAsync request.</summary>
file sealed class ConditioningCapturingGateway : IImageGenerationGateway
{
    private readonly ImageModelCapabilities _caps;
    private int _describeCallCount;
    private ImageGenerationRequest? _capturedRequest;

    public int DescribeCallCount => _describeCallCount;
    public ImageGenerationRequest? CapturedRequest => _capturedRequest;

    public ConditioningCapturingGateway(ImageModelCapabilities caps) => _caps = caps;

    public Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request, CancellationToken ct = default)
    {
        _capturedRequest = request;
        var result = new ImageGenerationResult([1, 2, 3], "image/png", "openai", "dall-e-3");
        return Task.FromResult(Result.Success(result));
    }

    public Task<Result<ImageModelCapabilities>> DescribeAsync(
        Guid organizationId, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _describeCallCount);
        return Task.FromResult(Result.Success(_caps));
    }
}

/// <summary>Tracks DescribeAsync calls; GenerateAsync always succeeds.</summary>
file sealed class ConditioningTrackingGateway : IImageGenerationGateway
{
    private readonly ImageModelCapabilities _caps;
    private int _describeCallCount;

    public int DescribeCallCount => _describeCallCount;

    public ConditioningTrackingGateway(ImageModelCapabilities caps) => _caps = caps;

    public Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request, CancellationToken ct = default)
    {
        var result = new ImageGenerationResult([1, 2, 3], "image/png", "openai", "dall-e-3");
        return Task.FromResult(Result.Success(result));
    }

    public Task<Result<ImageModelCapabilities>> DescribeAsync(
        Guid organizationId, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _describeCallCount);
        return Task.FromResult(Result.Success(_caps));
    }
}

/// <summary>Always fails the guardrail — used to test exhaustion / CSS fallback.</summary>
file sealed class ConditioningAlwaysFailValidator : IImageGuardrailValidator
{
    public Result<ImageGenerationResult> Validate(ImageGenerationResult result)
        => Result.Failure<ImageGenerationResult>(
            Error.Failure("guardrail.always-fail", "test guardrail always fails"));
}

file sealed class ConditioningStaticPromptWriter : IImagePromptWriter
{
    private readonly string _prompt;
    public ConditioningStaticPromptWriter(string prompt) => _prompt = prompt;
    public Task<Result<string>> WriteAsync(ImagePromptRequest request, CancellationToken ct = default)
        => Task.FromResult(Result.Success(_prompt));
}

file sealed class ConditioningSimpleStorage : IGeneratedAssetStorage
{
    private readonly string _url;
    public ConditioningSimpleStorage(string url) => _url = url;

    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
        => Task.FromResult(new StoredAssetRef(_url, content.Mime, content.Bytes.Length, "hash"));

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
        => Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}
