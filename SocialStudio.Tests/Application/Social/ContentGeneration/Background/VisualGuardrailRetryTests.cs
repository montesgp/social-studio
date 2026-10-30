using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Background;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentGeneration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// WU-4 RED tests (task 4.8): guardrail retry logic in
/// ClientAestheticImageBackgroundService (CAC-IMG-03).
///
/// CAC-IMG-03-a: first render passes the image guardrail validator → accepted; no retry.
/// CAC-IMG-03-b: first render fails; second render passes within budget → second result accepted.
/// CAC-IMG-03-c: all retries exhausted → slide degrades to CSS fallback; failure logged;
///   job NOT marked failed (slide absent from map, service returns normally).
///
/// Refs: CAC-IMG-03; design §3.4; tasks 4.8.
/// </summary>
public sealed class VisualGuardrailRetryTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid FormatResultId = Guid.NewGuid();

    // -----------------------------------------------------------------------
    // CAC-IMG-03-a: first render passes → no retry (single GenerateAsync call)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgrounds_ValidatorPassesOnFirstAttempt_NoRetry()
    {
        var imageGateway = new RetryCountingGateway(
            [Result.Success(MakeImage()), Result.Success(MakeImage())]);
        var validator = new RetryConfigurableValidator([true]); // pass on first call
        var sut = BuildSut(imageGateway, validator);

        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([1]), CancellationToken.None);

        Assert.Contains(1, result.Keys);
        Assert.Equal(1, imageGateway.CallCount); // exactly one generation
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-03-b: first render fails validator, second passes → second result accepted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgrounds_ValidatorFailsThenPasses_SecondResultAccepted()
    {
        var imageGateway = new RetryCountingGateway(
            [Result.Success(MakeImage()), Result.Success(MakeImage())]);
        var validator = new RetryConfigurableValidator([false, true]); // fail then pass
        var sut = BuildSut(imageGateway, validator);

        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([1]), CancellationToken.None);

        Assert.Contains(1, result.Keys); // slide accepted on second attempt
        Assert.Equal(2, imageGateway.CallCount); // two generation calls
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-03-c: all retries exhausted → slide absent from map
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgrounds_AllRetriesExhausted_SlideAbsentFromMap()
    {
        // All generation attempts produce an image that fails the validator
        var imageGateway = new RetryCountingGateway(
            [Result.Success(MakeImage()), Result.Success(MakeImage()), Result.Success(MakeImage())]);
        var validator = new RetryConfigurableValidator([false, false, false]); // always fail
        var sut = BuildSut(imageGateway, validator);

        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([1]), CancellationToken.None);

        // Slide must be absent — degrades to CSS
        Assert.DoesNotContain(1, result.Keys);
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-03-c: all retries exhausted → warning logged with reason
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgrounds_AllRetriesExhausted_WarningIsLogged()
    {
        var imageGateway = new RetryCountingGateway(
            [Result.Success(MakeImage()), Result.Success(MakeImage()), Result.Success(MakeImage())]);
        var validator = new RetryConfigurableValidator([false, false, false]);
        var captureLogger = new RetryWarningCaptureLogger();
        var sut = BuildSut(imageGateway, validator, captureLogger);

        await sut.BuildCreativeBackgroundsAsync(BuildRequest([1]), CancellationToken.None);

        // Must have logged a warning (guardrail exhaustion)
        Assert.True(captureLogger.HasWarning,
            "Expected a warning log when guardrail retries are exhausted.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ClientAestheticImageBackgroundService BuildSut(
        IImageGenerationGateway imageGateway,
        IImageGuardrailValidator validator,
        ILogger<ClientAestheticImageBackgroundService>? logger = null)
    {
        return new ClientAestheticImageBackgroundService(
            new RetryPromptWriter("background prompt"),
            imageGateway,
            new RetrySimpleStorage("https://cdn.example.com/img.png"),
            logger ?? NullLogger<ClientAestheticImageBackgroundService>.Instance,
            guardrailValidator: validator);
    }

    private static ImageGenerationResult MakeImage() =>
        new([1, 2, 3], "image/png", "openai", "dall-e-3");

    private static ClientAestheticImageRequest BuildRequest(IReadOnlyList<int> creativeSlideIndices) =>
        new(
            OrganizationId: OrgId,
            ClientId: ClientId,
            JobId: JobId,
            FormatResultId: FormatResultId,
            FormatKey: "carousel-4",
            SlideCount: 5,
            CreativeSlideIndices: creativeSlideIndices,
            Aesthetic: new ClientAestheticProfile(
                BackgroundDescriptors: ["dark botanical"],
                VoiceTone: "warm",
                AverageSlidesPerCarousel: 5.0,
                AnalyzedAtUtc: DateTime.UtcNow),
            PerSlideCopySummaries: ["copy 1", "copy 2", "copy 3", "copy 4", "copy 5"]);
}

// -----------------------------------------------------------------------
// Test doubles
// -----------------------------------------------------------------------

/// <summary>Returns a pre-configured sequence of results per successive GenerateAsync call.</summary>
file sealed class RetryCountingGateway : IImageGenerationGateway
{
    private readonly IReadOnlyList<Result<ImageGenerationResult>> _results;
    private int _callIndex;
    public int CallCount => _callIndex;

    public RetryCountingGateway(IReadOnlyList<Result<ImageGenerationResult>> results)
        => _results = results;

    public Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request, CancellationToken ct = default)
    {
        var idx = Interlocked.Increment(ref _callIndex) - 1;
        var result = idx < _results.Count
            ? _results[idx]
            : Result.Failure<ImageGenerationResult>(
                Error.Failure("image-gen.exhausted", "no more configured results"));
        return Task.FromResult(result);
    }

    public Task<Result<ImageModelCapabilities>> DescribeAsync(
        Guid organizationId, CancellationToken ct = default)
        => Task.FromResult(Result.Success(new ImageModelCapabilities(false, 0)));
}

/// <summary>Validates images based on a pre-configured pass/fail sequence (per-call).</summary>
file sealed class RetryConfigurableValidator : IImageGuardrailValidator
{
    private readonly IReadOnlyList<bool> _results;
    private int _callIndex;

    public RetryConfigurableValidator(IReadOnlyList<bool> results) => _results = results;

    public Result<ImageGenerationResult> Validate(ImageGenerationResult result)
    {
        var idx = Interlocked.Increment(ref _callIndex) - 1;
        var pass = idx < _results.Count && _results[idx];
        return pass
            ? Result.Success(result)
            : Result.Failure<ImageGenerationResult>(
                Error.Failure("guardrail.failed", "image guardrail validation failed"));
    }
}

file sealed class RetryWarningCaptureLogger : ILogger<ClientAestheticImageBackgroundService>
{
    private bool _hasWarning;
    public bool HasWarning => _hasWarning;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Warning)
        {
            _hasWarning = true;
        }
    }
}

file sealed class RetryPromptWriter : IImagePromptWriter
{
    private readonly string _prompt;
    public RetryPromptWriter(string prompt) => _prompt = prompt;
    public Task<Result<string>> WriteAsync(ImagePromptRequest request, CancellationToken ct = default)
        => Task.FromResult(Result.Success(_prompt));
}

file sealed class RetrySimpleStorage : IGeneratedAssetStorage
{
    private readonly string _url;
    public RetrySimpleStorage(string url) => _url = url;

    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
        => Task.FromResult(new StoredAssetRef(_url, content.Mime, content.Bytes.Length, "hash"));

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
        => Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}
