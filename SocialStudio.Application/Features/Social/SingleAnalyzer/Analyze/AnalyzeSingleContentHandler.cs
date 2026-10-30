using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.ContentAnalysis;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.SingleAnalyzer;
using MediatR;

namespace SocialStudio.Application.Features.Social.SingleAnalyzer.Analyze;

/// <summary>
/// Starts an analysis: validates + pre-flights + creates the record (Requested), then enqueues the
/// heavy work to run in the background. Returns immediately with the analysis id/status so the
/// client can poll — the long extraction + AI run is decoupled from this HTTP request.
/// </summary>
public sealed class AnalyzeSingleContentHandler
    : IRequestHandler<AnalyzeSingleContentCommand, Result<SingleAnalyzerStatusResponse>>
{
    private readonly ISingleContentAnalyzer _analyzer;
    private readonly ISingleAnalysisJobQueue _queue;

    public AnalyzeSingleContentHandler(ISingleContentAnalyzer analyzer, ISingleAnalysisJobQueue queue)
    {
        _analyzer = analyzer;
        _queue = queue;
    }

    public async Task<Result<SingleAnalyzerStatusResponse>> Handle(
        AnalyzeSingleContentCommand request,
        CancellationToken cancellationToken)
    {
        var started = await _analyzer.StartAsync(
            new SingleContentAnalysisRequest(
                request.OrganizationId,
                request.UserId,
                request.Url,
                request.ClientId,
                request.PreferredExtractionProviderKey),
            cancellationToken);

        if (started.IsFailure)
        {
            return started;
        }

        // StartAsync's org-scoped duplicate gate (SingleContentAnalyzer.CheckOrgDuplicateAsync) may
        // have resumed an existing Failed-with-snapshot analysis instead of creating a new one — it
        // signals that by returning the record already flipped to Analyzing. In that case we must
        // enqueue RetryAi (reuse the persisted snapshot, no extraction call) instead of a full run.
        var kind = started.Value.Status == SingleContentAnalysisStatus.Analyzing
            ? SingleAnalysisJobKind.RetryAi
            : SingleAnalysisJobKind.Full;

        await _queue.EnqueueAsync(
            new SingleAnalysisJob(started.Value.Id, started.Value.OrganizationId, kind),
            cancellationToken);

        return started;
    }
}

/// <summary>Reads the current status/result of an analysis for polling.</summary>
public sealed class GetSingleContentAnalysisHandler
    : IRequestHandler<GetSingleContentAnalysisQuery, Result<SingleAnalyzerStatusResponse>>
{
    private readonly ISingleContentAnalyzer _analyzer;

    public GetSingleContentAnalysisHandler(ISingleContentAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }

    public Task<Result<SingleAnalyzerStatusResponse>> Handle(
        GetSingleContentAnalysisQuery request,
        CancellationToken cancellationToken) =>
        _analyzer.GetAsync(request.AnalysisId, request.OrganizationId, cancellationToken);
}

/// <summary>
/// Re-runs only the AI stage of a previously-failed analysis. Validates the record exists and
/// has a persisted snapshot, flips status to Analyzing (so the polling client doesn't read a
/// stale Failed and bail out), and enqueues a RetryAi job for the background worker.
/// </summary>
public sealed class RetryAiAnalysisHandler
    : IRequestHandler<RetryAiAnalysisCommand, Result<SingleAnalyzerStatusResponse>>
{
    private readonly ISingleContentAnalysisRepository _analyses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ISingleAnalysisJobQueue _queue;

    public RetryAiAnalysisHandler(
        ISingleContentAnalysisRepository analyses,
        IUnitOfWork unitOfWork,
        IClock clock,
        ISingleAnalysisJobQueue queue)
    {
        _analyses = analyses;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _queue = queue;
    }

    public async Task<Result<SingleAnalyzerStatusResponse>> Handle(
        RetryAiAnalysisCommand request,
        CancellationToken cancellationToken)
    {
        var analysis = await _analyses.GetByIdAsync(request.AnalysisId, request.OrganizationId, cancellationToken);
        if (analysis is null)
        {
            return Result.Failure<SingleAnalyzerStatusResponse>(SingleAnalyzerErrors.NotFound);
        }

        if (analysis.AiAnalysisFailedAttemptCount >= SingleContentAnalyzer.MaxAiAnalysisFailedAttempts)
        {
            return Result.Failure<SingleAnalyzerStatusResponse>(SingleAnalyzerErrors.AiRetryBlocked);
        }

        if (string.IsNullOrWhiteSpace(analysis.ContentSnapshotJson))
        {
            // No persisted snapshot → can't retry AI in isolation. Caller should re-submit the URL.
            return Result.Failure<SingleAnalyzerStatusResponse>(SingleAnalyzerErrors.AnalysisFailed);
        }

        // Flip to Analyzing IMMEDIATELY so the polling client doesn't read the still-Failed status
        // and conclude the run is over. The worker will pick up the job and finish the transition
        // to Analyzed (or Failed) from there.
        var now = _clock.UtcNow;
        var pending = analysis with
        {
            Status = SingleContentAnalysisStatus.Analyzing,
            FailureCode = null,
            FailureMessageKey = null,
            UpdatedAtUtc = now,
            CompletedAtUtc = null
        };

        await _analyses.UpdateAsync(pending, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _queue.EnqueueAsync(
            new SingleAnalysisJob(pending.Id, pending.OrganizationId, SingleAnalysisJobKind.RetryAi),
            cancellationToken);

        return Result.Success(SingleAnalyzerStatusResponse.FromRecord(pending));
    }
}
