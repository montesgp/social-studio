using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Status;

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
    // Human/technical failure reason (e.g. "render_failed[flyer: guardrail-failed:
    // visual-guardrail.off-palette: …]") so the UI can show actionable guidance and route to
    // support instead of nudging a blind, token-burning retry. Optional/last so existing
    // positional constructions keep compiling.
    string? StatusReason = null);

/// <summary>
/// Handles <see cref="ContentGenerationJobStatusQuery"/>: loads the job and its format results.
/// Returns NotFound for cross-tenant or unknown job ids.
/// </summary>
public sealed class ContentGenerationJobStatusQueryHandler
    : IRequestHandler<ContentGenerationJobStatusQuery, Result<ContentGenerationJobStatusResponse>>
{
    private readonly IContentGenerationJobRepository _jobRepository;
    private readonly IContentGenerationFormatResultRepository _formatResultRepository;

    public ContentGenerationJobStatusQueryHandler(
        IContentGenerationJobRepository jobRepository,
        IContentGenerationFormatResultRepository formatResultRepository)
    {
        ArgumentNullException.ThrowIfNull(jobRepository);
        ArgumentNullException.ThrowIfNull(formatResultRepository);

        _jobRepository = jobRepository;
        _formatResultRepository = formatResultRepository;
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

        return Result.Success(new ContentGenerationJobStatusResponse(
            JobId: job.Id,
            Status: job.Status,
            PerFormatStatus: perFormatStatus,
            ManifestJson: job.ManifestJson,
            StatusReason: job.StatusReason));
    }
}
