using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Background;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentGeneration;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// 6.8 RED → 6.9 GREEN: per-slide telemetry written by
/// <see cref="ClientAestheticImageBackgroundService"/> when an optional
/// <see cref="IIntegrationExecutionAttemptRepository"/> is injected.
///
/// Covers CAC-INT-01-a (success row), CAC-INT-01-b (CSS fallback row), and
/// CAC-INT-01-c (telemetry write failure non-fatal).
/// </summary>
public sealed class PerSlideDegradationTelemetryTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid FormatResultId = Guid.NewGuid();

    // -----------------------------------------------------------------------
    // CAC-INT-01-a: successful image gen → Succeeded telemetry row per slide
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgroundsAsync_Success_WritesSuceedRowWithSlideIndex()
    {
        var repo = new SpyAttemptRepository();
        var sut = BuildSut(new SucceedingImageGateway(), repo);

        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([3]), CancellationToken.None);

        // THEN: one success row
        Assert.Single(repo.Attempts);
        var row = repo.Attempts[0];
        Assert.Equal(IntegrationExecutionAttemptStatus.Succeeded, row.Status);
        Assert.Equal(ClientAestheticImageBackgroundService.SlideCapabilityKey, row.CapabilityKey);
        Assert.Equal(ClientAestheticImageBackgroundService.SlideProviderKey, row.ProviderKey);
        Assert.Equal(FormatResultId, row.CorrelationId);
        Assert.Equal(ClientAestheticImageBackgroundService.SlideCorrelationKind, row.CorrelationKind);
        Assert.Null(row.AnalysisId);

        // DiagnosticsJson contains slideIndex
        Assert.NotNull(row.DiagnosticsJson);
        using var doc = JsonDocument.Parse(row.DiagnosticsJson!);
        Assert.Equal(3, doc.RootElement.GetProperty("slideIndex").GetInt32());

        // Slide present in the result map
        Assert.Contains(3, result.Keys);
    }

    // -----------------------------------------------------------------------
    // CAC-INT-01-b: CSS fallback (image-gen exhausted) → Failed row with reason
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgroundsAsync_ImageGenFailed_WritesCssFallbackRow()
    {
        var repo = new SpyAttemptRepository();
        var sut = BuildSut(new FailingImageGateway(), repo);

        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([4]), CancellationToken.None);

        // Slide absent (CSS fallback)
        Assert.DoesNotContain(4, result.Keys);

        // THEN: one failed row
        Assert.Single(repo.Attempts);
        var row = repo.Attempts[0];
        Assert.Equal(IntegrationExecutionAttemptStatus.Failed, row.Status);
        Assert.Equal(ClientAestheticImageBackgroundService.SlideCapabilityKey, row.CapabilityKey);
        Assert.Equal(FormatResultId, row.CorrelationId);
        Assert.NotNull(row.FailureCode);

        // DiagnosticsJson contains slideIndex, outcome = degraded-to-css, reason
        Assert.NotNull(row.DiagnosticsJson);
        using var doc = JsonDocument.Parse(row.DiagnosticsJson!);
        Assert.Equal(4, doc.RootElement.GetProperty("slideIndex").GetInt32());
        Assert.Equal("degraded-to-css", doc.RootElement.GetProperty("outcome").GetString());
        Assert.True(doc.RootElement.TryGetProperty("reason", out var reasonProp));
        Assert.False(string.IsNullOrEmpty(reasonProp.GetString()));
    }

    // -----------------------------------------------------------------------
    // CAC-INT-01-c: telemetry write failure → caught; generation job continues;
    //               successful slide still returned in the result map.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgroundsAsync_TelemetryWriteThrows_SlideStillReturned()
    {
        var throwingRepo = new ThrowingAttemptRepository();
        var sut = BuildSut(new SucceedingImageGateway(), throwingRepo);

        // Must not throw despite the repository throwing
        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([1]), CancellationToken.None);

        // Slide is still generated and returned — telemetry failure is non-fatal
        Assert.Contains(1, result.Keys);
    }

    // -----------------------------------------------------------------------
    // No repository injected → no rows written (optional wiring)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildCreativeBackgroundsAsync_NoRepository_StillSucceeds()
    {
        // No repo → works exactly as before (backward compat for call sites that omit it)
        var sut = BuildSut(new SucceedingImageGateway(), attemptRepository: null);

        var result = await sut.BuildCreativeBackgroundsAsync(BuildRequest([2]), CancellationToken.None);

        Assert.Contains(2, result.Keys);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

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
            PerSlideCopySummaries: ["s1", "s2", "s3", "s4", "s5"]);

    private static ClientAestheticImageBackgroundService BuildSut(
        IImageGenerationGateway gateway,
        IIntegrationExecutionAttemptRepository? attemptRepository)
    {
        return new ClientAestheticImageBackgroundService(
            promptWriter: new StaticSuccessPromptWriter(),
            imageGateway: gateway,
            storage: new SimpleCapturingStorage(),
            logger: NullLogger<ClientAestheticImageBackgroundService>.Instance,
            guardrailValidator: null,
            attemptRepository: attemptRepository);
    }
}

// -----------------------------------------------------------------------
// Test doubles — file-scoped
// -----------------------------------------------------------------------

file sealed class SpyAttemptRepository : IIntegrationExecutionAttemptRepository
{
    public List<IntegrationExecutionAttemptRecord> Attempts { get; } = [];

    public Task AddAsync(IntegrationExecutionAttemptRecord attempt, CancellationToken cancellationToken = default)
    {
        Attempts.Add(attempt);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IntegrationExecutionAttemptRecord>> ListByAnalysisIdAsync(
        Guid analysisId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IntegrationExecutionAttemptRecord>>(Array.Empty<IntegrationExecutionAttemptRecord>());
}

file sealed class ThrowingAttemptRepository : IIntegrationExecutionAttemptRepository
{
    public Task AddAsync(IntegrationExecutionAttemptRecord attempt, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated telemetry write failure");

    public Task<IReadOnlyList<IntegrationExecutionAttemptRecord>> ListByAnalysisIdAsync(
        Guid analysisId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IntegrationExecutionAttemptRecord>>(Array.Empty<IntegrationExecutionAttemptRecord>());
}

file sealed class SucceedingImageGateway : IImageGenerationGateway
{
    public Task<Result<ImageGenerationResult>> GenerateAsync(ImageGenerationRequest request, CancellationToken ct = default) =>
        Task.FromResult(Result.Success(new ImageGenerationResult([1, 2, 3], "image/png", "openai", "dall-e-3")));

    public Task<Result<ImageModelCapabilities>> DescribeAsync(Guid organizationId, CancellationToken ct = default) =>
        Task.FromResult(Result.Success(new ImageModelCapabilities(false, 0)));
}

file sealed class FailingImageGateway : IImageGenerationGateway
{
    public Task<Result<ImageGenerationResult>> GenerateAsync(ImageGenerationRequest request, CancellationToken ct = default) =>
        Task.FromResult(Result.Failure<ImageGenerationResult>(
            Error.Failure("image-gen.provider-error", "Simulated image generation failure")));

    public Task<Result<ImageModelCapabilities>> DescribeAsync(Guid organizationId, CancellationToken ct = default) =>
        Task.FromResult(Result.Success(new ImageModelCapabilities(false, 0)));
}

file sealed class StaticSuccessPromptWriter : IImagePromptWriter
{
    public Task<Result<string>> WriteAsync(ImagePromptRequest request, CancellationToken ct = default) =>
        Task.FromResult(Result.Success("a beautiful AI-generated background image"));
}

file sealed class SimpleCapturingStorage : IGeneratedAssetStorage
{
    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct) =>
        Task.FromResult(new StoredAssetRef($"orgs/{key.OrganizationId}/{key.RelativePath}", content.Mime, content.Bytes.Length, "hash"));

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);

    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct) =>
        Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));

    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}
