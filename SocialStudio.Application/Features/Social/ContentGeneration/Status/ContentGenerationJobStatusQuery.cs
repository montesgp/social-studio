using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.GeneratePackage;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Status;

/// <summary>
/// Query to get the current status of a content generation job, including
/// per-format substatus and the manifest when the job is completed.
/// Tenant-scoped: cross-tenant job ids return NotFound.
/// </summary>
public sealed record ContentGenerationJobStatusQuery(
    Guid JobId,
    Guid OrganizationId) : IRequest<Result<ContentGenerationJobStatusResponse>>;

/// <summary>
/// Response containing job status, per-format substatus, and optional manifest.
/// </summary>
public sealed record ContentGenerationJobStatusResponse(
    Guid JobId,
    string Status,
    IReadOnlyDictionary<string, string> PerFormatStatus,
    string? ManifestJson,
    // Human/technical failure reason (e.g. "render_failed[story: guardrail-failed:
    // visual-guardrail.off-palette: …]") so the UI can show actionable guidance and route to
    // support instead of nudging a blind, token-burning retry. Optional/last so existing
    // positional constructions keep compiling.
    string? StatusReason = null,
    // Cost-control telemetry: failed attempts per format across jobs for this analysis/client.
    // The UI uses this to disable regeneration controls after the backend threshold is reached.
    IReadOnlyDictionary<string, int>? PerFormatFailureCounts = null,
    GeneratedPackageActionState? PackageActionState = null);

/// <summary>
/// Handles <see cref="ContentGenerationJobStatusQuery"/>: loads the job and its format results.
/// Returns NotFound for cross-tenant or unknown job ids.
/// </summary>
public sealed class ContentGenerationJobStatusQueryHandler
    : IRequestHandler<ContentGenerationJobStatusQuery, Result<ContentGenerationJobStatusResponse>>
{
    private readonly IContentGenerationJobRepository _jobRepository;
    private readonly IContentGenerationFormatResultRepository _formatResultRepository;
    private readonly IContentGenerationFailurePolicy? _failurePolicy;
    private readonly IGeneratedPackageRepository? _packageRepository;

    public ContentGenerationJobStatusQueryHandler(
        IContentGenerationJobRepository jobRepository,
        IContentGenerationFormatResultRepository formatResultRepository,
        IContentGenerationFailurePolicy? failurePolicy = null,
        IGeneratedPackageRepository? packageRepository = null)
    {
        ArgumentNullException.ThrowIfNull(jobRepository);
        ArgumentNullException.ThrowIfNull(formatResultRepository);

        _jobRepository = jobRepository;
        _formatResultRepository = formatResultRepository;
        _failurePolicy = failurePolicy;
        _packageRepository = packageRepository;
    }

    public async Task<Result<ContentGenerationJobStatusResponse>> Handle(
        ContentGenerationJobStatusQuery query,
        CancellationToken cancellationToken)
    {
        // Tenant-scoped lookup — returns null for cross-tenant ids
        var job = await _jobRepository.GetByIdAsync(
            query.JobId,
            query.OrganizationId,
            cancellationToken);

        if (job is null)
        {
            return Result.Failure<ContentGenerationJobStatusResponse>(
                ContentGenerationErrors.JobNotFound);
        }

        var formatResults = await _formatResultRepository.ListByJobAsync(job.Id, cancellationToken);

        var perFormatStatus = formatResults.ToDictionary(r => r.FormatKey, r => r.Status);
        var failureCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (_failurePolicy is not null && job.ClientId is { } clientId && clientId != Guid.Empty)
        {
            var formatKeys = GenerateContentPackageCommandHandler.KnownFormatKeys
                .ToList();

            var counted = await _failurePolicy.CountFailedAttemptsByFormatAsync(
                job.OrganizationId,
                job.AnalysisId,
                clientId,
                formatKeys,
                cancellationToken);

            foreach (var (formatKey, count) in counted)
            {
                failureCounts[formatKey] = count;
            }
        }

        GeneratedPackageActionState? actionState = null;
        if (_packageRepository is not null)
        {
            var package = await _packageRepository.GetByJobIdAsync(
                job.Id,
                job.OrganizationId,
                cancellationToken);

            if (package is not null)
            {
                actionState = new GeneratedPackageActionState(
                    package.DownloadCount,
                    package.LastDownloadedAtUtc,
                    package.CanvaSendCount,
                    package.LastSentToCanvaAtUtc);
            }
        }

        return Result.Success(new ContentGenerationJobStatusResponse(
            JobId: job.Id,
            Status: job.Status,
            PerFormatStatus: perFormatStatus,
            ManifestJson: job.ManifestJson,
            StatusReason: job.StatusReason,
            PerFormatFailureCounts: failureCounts,
            PackageActionState: actionState));
    }
}
