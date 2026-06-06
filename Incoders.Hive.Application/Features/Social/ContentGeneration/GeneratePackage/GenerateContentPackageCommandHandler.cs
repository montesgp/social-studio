using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;

/// <summary>
/// Handles <see cref="GenerateContentPackageCommand"/>:
/// 1. Validates the analysis exists and belongs to the calling tenant.
/// 2. Validates requested formats against the known whitelist.
/// 3. Persists a <see cref="ContentGenerationJob"/> with status <c>Queued</c>.
/// 4. Enqueues the job id to <see cref="IContentGenerationJobQueue"/>.
/// 5. Returns the job id and a status poll URL.
/// </summary>
public sealed class GenerateContentPackageCommandHandler
    : IRequestHandler<GenerateContentPackageCommand, Result<GenerateContentPackageResponse>>
{
    internal const int MaxRegenerationObjectionLength = 1000;

    /// <summary>The 7 supported format keys.</summary>
    internal static readonly IReadOnlySet<string> KnownFormatKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "carousel",
        "flyer",
        "reel-script",
        "content-idea",
        "cta-variation",
        "caption",
        "hook",
    };

    private readonly ISingleContentAnalysisRepository _analysisRepository;
    private readonly IContentGenerationJobRepository _jobRepository;
    private readonly IContentGenerationJobQueue _queue;

    public GenerateContentPackageCommandHandler(
        ISingleContentAnalysisRepository analysisRepository,
        IContentGenerationJobRepository jobRepository,
        IContentGenerationJobQueue queue)
    {
        ArgumentNullException.ThrowIfNull(analysisRepository);
        ArgumentNullException.ThrowIfNull(jobRepository);
        ArgumentNullException.ThrowIfNull(queue);

        _analysisRepository = analysisRepository;
        _jobRepository = jobRepository;
        _queue = queue;
    }

    public async Task<Result<GenerateContentPackageResponse>> Handle(
        GenerateContentPackageCommand command,
        CancellationToken cancellationToken)
    {
        var normalizedRegenerationObjection = string.IsNullOrWhiteSpace(command.RegenerationObjection)
            ? null
            : command.RegenerationObjection.Trim();

        if (normalizedRegenerationObjection is { Length: > MaxRegenerationObjectionLength })
        {
            return Result.Failure<GenerateContentPackageResponse>(
                ContentGenerationErrors.RegenerationObjectionTooLong(MaxRegenerationObjectionLength));
        }

        // Validate: at least one format requested
        if (command.RequestedFormats is null || command.RequestedFormats.Count == 0)
        {
            return Result.Failure<GenerateContentPackageResponse>(
                ContentGenerationErrors.EmptyFormats);
        }

        // Validate: all formats are from the known whitelist
        var unknownFormats = command.RequestedFormats
            .Where(f => !KnownFormatKeys.Contains(f))
            .ToList();

        if (unknownFormats.Count > 0)
        {
            return Result.Failure<GenerateContentPackageResponse>(
                ContentGenerationErrors.UnknownFormatKey(unknownFormats[0]));
        }

        // Load analysis — tenant-scoped (repo filters by OrganizationId)
        var analysis = await _analysisRepository.GetByIdAsync(
            command.AnalysisId,
            command.OrganizationId,
            cancellationToken);

        if (analysis is null)
        {
            return Result.Failure<GenerateContentPackageResponse>(
                ContentGenerationErrors.AnalysisNotFound);
        }

        var effectiveClientId = command.ClientId ?? analysis.ClientId;
        if (effectiveClientId is null || effectiveClientId == Guid.Empty)
        {
            return Result.Failure<GenerateContentPackageResponse>(
                ContentGenerationErrors.ClientRequired);
        }

        // Create and persist the job
        var jobId = Guid.NewGuid();
        var correlationId = command.CorrelationId ?? Guid.NewGuid();

        var job = new ContentGenerationJob(
            Id: jobId,
            OrganizationId: command.OrganizationId,
            ClientId: effectiveClientId,
            AnalysisId: command.AnalysisId,
            RequestedFormats: command.RequestedFormats,
            Status: "Queued",
            StatusReason: null,
            ErrorCode: null,
            BrandSnapshotJson: "{}",  // brand snapshot loaded by the worker/orchestrator at runtime
            ManifestJson: null,
            CorrelationId: correlationId,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            StartedAt: null,
            CompletedAt: null,
            RegenerationObjection: normalizedRegenerationObjection);

        await _jobRepository.InsertAsync(job, cancellationToken);

        // Enqueue the job id for async processing
        await _queue.EnqueueAsync(jobId, cancellationToken);

        var statusUrl = $"/api/social/content-generation-jobs/{jobId}/status";
        return Result.Success(new GenerateContentPackageResponse(jobId, statusUrl));
    }
}

/// <summary>Domain errors specific to the content generation feature.</summary>
public static class ContentGenerationErrors
{
    public static readonly Error EmptyFormats =
        Error.Validation("content-gen.empty-formats", "At least one format must be requested.");

    public static readonly Error AnalysisNotFound =
        Error.NotFound("content-gen.analysis-not-found", "The specified analysis was not found.");

    public static readonly Error JobNotFound =
        Error.NotFound("content-gen.job-not-found", "The specified job was not found.");

    public static readonly Error ClientRequired =
        Error.Validation("content-gen.client-required", "A client is required to generate branded content.");

    public static readonly Error PackageNotReady =
        Error.NotFound("content-gen.package-not-ready", "The package is not yet available for download.");

    public static Error RegenerationObjectionTooLong(int maxLength) =>
        Error.Validation(
            "content-gen.regeneration-objection-too-long",
            $"Regeneration objection must be {maxLength} characters or fewer.");

    public static Error UnknownFormatKey(string key) =>
        Error.Validation("content-gen.unknown-format-key", $"Unknown format key: {key}");

    /// <summary>
    /// The format result does not have persisted markup (predates B3 or render failed).
    /// Spec PPTX-02-d: return this as a clear handled error, never re-design or throw.
    /// Maps to HTTP 409 Conflict via <c>ErrorType.Conflict</c>.
    /// </summary>
    public static readonly Error NotExportable =
        Error.Conflict(
            "content-gen.not-exportable",
            "This format does not have a persisted composition spec and cannot be exported as PPTX.");
}
