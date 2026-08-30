using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;

/// <summary>
/// Re-runs ONLY the AI stage (stages 5-7) of a previously-failed profile analysis,
/// reusing the persisted scrape data. No IProfileScrapeResolver call — no Apify cost.
/// </summary>
/// <remarks>
/// Pre-conditions (validated by the handler):
/// - The record must exist and be scoped to <paramref name="OrganizationId"/>.
/// - The record must be in <see cref="ProfileAnalysisStatus.Failed"/> status.
/// - The record must have persisted scrape data (ProfileSnapshotJson, RawPostsDatasetJson,
///   ServerDerivedStatsJson all non-null) — i.e. the failure occurred AFTER the scrape completed.
///   If any of these is missing the scrape itself failed and a full re-run is required (→ 422).
/// </remarks>
public sealed record RetryProfileAnalysisCommand(
    Guid AnalysisId,
    Guid OrganizationId) : IRequest<Result<AnalyzeProfileStatusResponse>>;

/// <summary>
/// Validates that the requested analysis can be retried using AI-only,
/// transitions it to <see cref="ProfileAnalysisStatus.Analyzing"/>,
/// and enqueues a <see cref="ProfileAnalysisJobKind.RetryAi"/> job.
/// Returns the updated status response immediately so the caller can poll.
/// </summary>
public sealed class RetryProfileAnalysisHandler
    : IRequestHandler<RetryProfileAnalysisCommand, Result<AnalyzeProfileStatusResponse>>
{
    private readonly IProfileAnalysisRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IProfileAnalysisJobQueue _queue;

    public RetryProfileAnalysisHandler(
        IProfileAnalysisRepository repository,
        IUnitOfWork unitOfWork,
        IClock clock,
        IProfileAnalysisJobQueue queue)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _queue = queue;
    }

    public async Task<Result<AnalyzeProfileStatusResponse>> Handle(
        RetryProfileAnalysisCommand request,
        CancellationToken cancellationToken)
    {
        var analysis = await _repository.GetByIdAsync(
            request.AnalysisId,
            request.OrganizationId,
            cancellationToken);

        if (analysis is null)
        {
            return Result.Failure<AnalyzeProfileStatusResponse>(ProfileAnalysisErrors.NotFound);
        }

        // Guard: must be Failed AND have persisted scrape data.
        if (analysis.Status != ProfileAnalysisStatus.Failed)
        {
            return Result.Failure<AnalyzeProfileStatusResponse>(ProfileAnalysisErrors.NotResumable);
        }

        if (string.IsNullOrWhiteSpace(analysis.ProfileSnapshotJson) ||
            string.IsNullOrWhiteSpace(analysis.RawPostsDatasetJson) ||
            string.IsNullOrWhiteSpace(analysis.ServerDerivedStatsJson))
        {
            // Scrape stage never completed — a full re-run is required.
            return Result.Failure<AnalyzeProfileStatusResponse>(ProfileAnalysisErrors.NotResumable);
        }

        // Flip immediately to Analyzing so polling clients don't read the stale Failed status
        // and incorrectly conclude the run is over. The background job transitions to Analyzed|Failed.
        analysis.RestartAnalysis(_clock.UtcNow);

        await _repository.UpdateAsync(analysis, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _queue.EnqueueAsync(
            new ProfileAnalysisJob(analysis.Id, analysis.OrganizationId, ProfileAnalysisJobKind.RetryAi),
            cancellationToken);

        return Result.Success(AnalyzeProfileStatusResponse.FromRecord(analysis));
    }
}
