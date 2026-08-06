using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentFormats;
using Incoders.Hive.Domain.Social.Clients;
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
    internal const int MaxFailedAttemptsPerFormat = 2;

    /// <summary>
    /// Catalog-derived whitelist of accepted format keys.
    /// Derived from <see cref="FormatCatalog.KnownKeys()"/> so any new catalog row is
    /// automatically accepted without a code change here.
    /// The legacy "carousel" alias is NOT included — bare "carousel" is rejected at the API
    /// (CRITICAL-2 / SCENARIO-04). The FE sends surface-scoped keys (story-carousel, feed-carousel).
    /// </summary>
    internal static readonly IReadOnlySet<string> KnownFormatKeys =
        FormatCatalog.KnownKeys().ToHashSet(StringComparer.OrdinalIgnoreCase);

    private readonly ISingleContentAnalysisRepository _analysisRepository;
    private readonly IContentGenerationJobRepository _jobRepository;
    private readonly IContentGenerationJobQueue _queue;
    private readonly IContentGenerationFailurePolicy? _failurePolicy;
    private readonly ISocialClientRepository? _clients;

    public GenerateContentPackageCommandHandler(
        ISingleContentAnalysisRepository analysisRepository,
        IContentGenerationJobRepository jobRepository,
        IContentGenerationJobQueue queue,
        IContentGenerationFailurePolicy? failurePolicy = null,
        ISocialClientRepository? clients = null)
    {
        ArgumentNullException.ThrowIfNull(analysisRepository);
        ArgumentNullException.ThrowIfNull(jobRepository);
        ArgumentNullException.ThrowIfNull(queue);

        _analysisRepository = analysisRepository;
        _jobRepository = jobRepository;
        _queue = queue;
        _failurePolicy = failurePolicy;
        _clients = clients;
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

        var normalizedRequestedFormats = command.RequestedFormats
            .Select(NormalizeWriteFormatKey)
            .Select(format => format.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Validate: all formats are from the known whitelist. Legacy single keys are normalized
        // above for compatibility; legacy "carousel" remains rejected on new writes.
        var unknownFormats = normalizedRequestedFormats
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

        // Per-client piece cap: trims the request BEFORE the job is persisted, so the job row, the
        // worker, and the eventual Content Bank all agree on how many pieces this run builds.
        // Order is preserved, so the formats the caller listed first are the ones that survive.
        if (_clients is not null)
        {
            var client = await _clients.GetByIdAsync(
                new ClientId(effectiveClientId.Value), command.OrganizationId, cancellationToken);

            if (client is not null)
            {
                normalizedRequestedFormats = client.ApplyPieceCap(normalizedRequestedFormats).ToList();
            }
        }

        if (_failurePolicy is not null)
        {
            var failedAttempts = await _failurePolicy.CountFailedAttemptsByFormatAsync(
                command.OrganizationId,
                command.AnalysisId,
                effectiveClientId.Value,
                normalizedRequestedFormats,
                cancellationToken);

            var blockedFormat = normalizedRequestedFormats
                .FirstOrDefault(format =>
                    failedAttempts.TryGetValue(format, out var count) &&
                    count >= MaxFailedAttemptsPerFormat);

            if (blockedFormat is not null)
            {
                return Result.Failure<GenerateContentPackageResponse>(
                    ContentGenerationErrors.FormatRegenerationBlocked(blockedFormat, MaxFailedAttemptsPerFormat));
            }
        }

        // Create and persist the job
        var jobId = Guid.NewGuid();
        var correlationId = command.CorrelationId ?? Guid.NewGuid();

        var job = new ContentGenerationJob(
            Id: jobId,
            OrganizationId: command.OrganizationId,
            ClientId: effectiveClientId,
            AnalysisId: command.AnalysisId,
            RequestedFormats: normalizedRequestedFormats,
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

    private static string NormalizeWriteFormatKey(string formatKey) =>
        formatKey.Trim().ToLowerInvariant() switch
        {
            "story" => "story-single",
            "feed-post" => "feed-single",
            _ => formatKey,
        };
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

    public static Error FormatRegenerationBlocked(string formatKey, int maxFailedAttempts) =>
        Error.Conflict(
            "content-gen.regeneration-blocked",
            $"Generation for {formatKey} is blocked after {maxFailedAttempts} failed attempts. Contact support before trying again.");
}
