using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.GeneratePackage;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Regenerate;

// ─── Request / Response ───────────────────────────────────────────────────────

/// <summary>
/// Regenerates a single format of an existing package by RE-RUNNING the real generation pipeline
/// (AI generation with the objection + ClientAesthetic enrichment + visual designer + render),
/// NOT a re-render of the cached output. Async: re-enqueues the existing job after clearing the
/// target format's cached AI payload and setting the objection, so the background processor
/// regenerates ONLY that format (other formats are reused from cache — no extra AI spend).
/// Cross-tenant or unknown format/job → NotFound.
/// </summary>
public sealed record RegenerateFormatAssetCommand(
    Guid AnalysisId,
    Guid OrganizationId,
    Guid ClientId,
    string FormatKey,
    string? Objection) : IRequest<Result<RegenerateFormatAssetResponse>>;

/// <summary>
/// Acknowledgement that regeneration was queued. The caller polls the job status (same flow as a
/// normal generation) to observe completion and fetch the new asset(s).
/// </summary>
public sealed record RegenerateFormatAssetResponse(
    Guid JobId,
    string FormatKey,
    string Status);

// ─── Handler ──────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="RegenerateFormatAssetCommand"/> by re-enqueuing the existing job so the full
/// generation pipeline regenerates the target format.
/// <para>
/// Algorithm:
/// 1. Tenant-scope: verify the analysis belongs to <c>OrganizationId</c>.
/// 2. Find the most-recent completed job for (analysisId, orgId, clientId).
/// 3. Confirm the target format has a stored result in that job.
/// 4. Clear that format result's cached AI payload (OutputJson) so the processor re-generates it;
///    other formats keep their cache and are only re-rendered (no extra AI spend).
/// 5. Set the job's regeneration objection.
/// 6. Reset the job to Queued and enqueue it for the background processor.
/// </para>
/// </summary>
public sealed class RegenerateFormatAssetCommandHandler
    : IRequestHandler<RegenerateFormatAssetCommand, Result<RegenerateFormatAssetResponse>>
{
    private readonly IContentGenerationFormatResultRepository _formatResultRepository;
    private readonly IContentGenerationJobRepository _jobRepository;
    private readonly ISingleContentAnalysisRepository _analysisRepository;
    private readonly IContentGenerationJobQueue _queue;

    public RegenerateFormatAssetCommandHandler(
        IContentGenerationFormatResultRepository formatResultRepository,
        IContentGenerationJobRepository jobRepository,
        ISingleContentAnalysisRepository analysisRepository,
        IContentGenerationJobQueue queue)
    {
        ArgumentNullException.ThrowIfNull(formatResultRepository);
        ArgumentNullException.ThrowIfNull(jobRepository);
        ArgumentNullException.ThrowIfNull(analysisRepository);
        ArgumentNullException.ThrowIfNull(queue);

        _formatResultRepository = formatResultRepository;
        _jobRepository = jobRepository;
        _analysisRepository = analysisRepository;
        _queue = queue;
    }

    public async Task<Result<RegenerateFormatAssetResponse>> Handle(
        RegenerateFormatAssetCommand command,
        CancellationToken cancellationToken)
    {
        const int maxObjectionLength = 1000;
        var objection = string.IsNullOrWhiteSpace(command.Objection)
            ? null
            : command.Objection.Trim();
        if (objection is not null && objection.Length > maxObjectionLength)
        {
            return Result.Failure<RegenerateFormatAssetResponse>(
                ContentGenerationErrors.RegenerationObjectionTooLong(maxObjectionLength));
        }

        // 1. Tenant-scope check
        var analysis = await _analysisRepository.GetByIdAsync(
            command.AnalysisId, command.OrganizationId, cancellationToken);
        if (analysis is null)
        {
            return Result.Failure<RegenerateFormatAssetResponse>(ContentGenerationErrors.AnalysisNotFound);
        }

        // 2. Latest completed job for this analysis + client
        var job = await _jobRepository.GetLatestByAnalysisAndClientAsync(
            command.AnalysisId, command.OrganizationId, command.ClientId, cancellationToken);
        if (job is null)
        {
            return Result.Failure<RegenerateFormatAssetResponse>(ContentGenerationErrors.JobNotFound);
        }

        // 3. Confirm the format exists in this job
        var formatResults = await _formatResultRepository.ListByJobAsync(job.Id, cancellationToken);
        var formatResult = formatResults.FirstOrDefault(r =>
            string.Equals(r.FormatKey, command.FormatKey, StringComparison.OrdinalIgnoreCase));
        if (formatResult is null)
        {
            return Result.Failure<RegenerateFormatAssetResponse>(
                Error.NotFound(
                    "content-gen.format-not-found",
                    $"No stored result found for format '{command.FormatKey}'."));
        }

        // 4. Clear the cached AI payload for the target format so the processor RE-GENERATES it
        //    (upsert keys by job+format; other formats keep their OutputJson and are only re-rendered).
        await _formatResultRepository.UpsertAsync(
            formatResult with
            {
                Status = "Queued",
                OutputJson = null,
                CompositionSpecJson = null,
                RenderedAssetUri = null,
                ErrorCode = null,
                UpdatedAt = DateTime.UtcNow,
            },
            cancellationToken);

        // 5. Set the objection on the job so the generators thread it into the prompt.
        await _jobRepository.SetRegenerationObjectionAsync(job.Id, objection, cancellationToken);

        // 6. Reset to Queued and re-enqueue for the background processor (full pipeline).
        await _jobRepository.UpdateStatusAsync(job.Id, "Queued", null, null, cancellationToken);
        await _queue.EnqueueAsync(job.Id, cancellationToken);

        return Result.Success(new RegenerateFormatAssetResponse(job.Id, command.FormatKey, "Queued"));
    }
}
