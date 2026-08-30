using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentFormats;
using Incoders.Hive.Domain.Social.Workflow;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Workflow.Enqueue;

/// <summary>
/// Validates and persists one candidate's format composition, then fans out one independent
/// analysis-then-batch sequence per accepted candidate onto <see cref="IContentWorkflowEnqueueQueue"/>.
/// A candidate that fails validation is REJECTED, not fatal to the request — its siblings still get
/// queued (D4: independence / partial failure).
/// </summary>
public sealed class EnqueueContentWorkflowCandidatesCommandHandler
    : IRequestHandler<EnqueueContentWorkflowCandidatesCommand, Result<EnqueueContentWorkflowCandidatesResponse>>
{
    private readonly IContentWorkflowSessionRepository _sessions;
    private readonly IContentWorkflowEnqueueQueue _queue;
    private readonly IClock _clock;
    private readonly ContentWorkflowEtaOptions _etaOptions;

    public EnqueueContentWorkflowCandidatesCommandHandler(
        IContentWorkflowSessionRepository sessions,
        IContentWorkflowEnqueueQueue queue,
        IClock clock,
        ContentWorkflowEtaOptions etaOptions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(etaOptions);

        _sessions = sessions;
        _queue = queue;
        _clock = clock;
        _etaOptions = etaOptions;
    }

    public async Task<Result<EnqueueContentWorkflowCandidatesResponse>> Handle(
        EnqueueContentWorkflowCandidatesCommand request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<EnqueueContentWorkflowCandidatesResponse>(ContentWorkflowErrors.OrganizationRequired);
        }

        if (request.Candidates.Count == 0)
        {
            return Result.Failure<EnqueueContentWorkflowCandidatesResponse>(ContentWorkflowErrors.CandidatesRequired);
        }

        var session = await _sessions.GetByIdAsync(request.WorkflowId, request.OrganizationId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<EnqueueContentWorkflowCandidatesResponse>(ContentWorkflowErrors.NotFound);
        }

        var results = new List<EnqueuedCandidateResult>(request.Candidates.Count);
        var totalPieces = 0;
        var nowUtc = _clock.UtcNow;

        foreach (var requested in request.Candidates)
        {
            var candidate = session.Candidates.FirstOrDefault(c => c.Id == requested.CandidateId);
            if (candidate is null)
            {
                results.Add(new EnqueuedCandidateResult(requested.CandidateId, false, WorkflowCandidateErrors.NotFoundInSession.Code));
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate.ExternalResultId))
            {
                results.Add(new EnqueuedCandidateResult(requested.CandidateId, false, WorkflowCandidateErrors.NotAnalyzable.Code));
                continue;
            }

            var unknownFormat = requested.Composition.FirstOrDefault(item => FormatCatalog.TryGet(item.FormatKey) is null);
            if (unknownFormat is not null)
            {
                results.Add(new EnqueuedCandidateResult(
                    requested.CandidateId,
                    false,
                    WorkflowCandidateErrors.UnknownFormat(unknownFormat.FormatKey).Code));
                continue;
            }

            var compositionTuples = requested.Composition
                .Select(item => (item.FormatKey, item.Count))
                .ToList();

            var requestedKey = WorkflowCandidateEnqueueKey.Create(request.WorkflowId, requested.CandidateId, compositionTuples);

            // Idempotent replay: the same composition re-submitted while the candidate has not reached
            // a terminal state is a no-op — accepted, but nothing is written or queued twice (double-click
            // must not double-bill Apify/AI).
            if (candidate.Composition.Count > 0 && !WorkflowCandidateTransition.IsTerminal(candidate.Status))
            {
                var existingKey = WorkflowCandidateEnqueueKey.Create(request.WorkflowId, requested.CandidateId, candidate.Composition);
                if (existingKey == requestedKey)
                {
                    results.Add(new EnqueuedCandidateResult(requested.CandidateId, true, null));
                    totalPieces += requested.Composition.Sum(item => item.Count);
                    continue;
                }
            }

            var compositionResult = candidate.SetComposition(compositionTuples);
            if (compositionResult.IsFailure)
            {
                results.Add(new EnqueuedCandidateResult(requested.CandidateId, false, compositionResult.Error.Code));
                continue;
            }

            await _sessions.SetCandidateCompositionAsync(
                candidate.Id,
                JsonSerializer.Serialize(requested.Composition),
                requestedKey,
                cancellationToken);

            // Marks the candidate as genuinely submitted to the pipeline (Discovered → Queued). Discovery
            // only ever persists `Discovered`; this is the one write path that transitions a candidate
            // into the enqueue-sequence status machine `UpdateCandidateStatusAsync`/`GetHistoryAsync`
            // read back. Idempotent replay above already returned early, so this only runs once per
            // genuinely new composition.
            await _sessions.UpdateCandidateStatusAsync(
                candidate.Id, WorkflowCandidateStatus.Queued, null, null, null, cancellationToken);

            await _sessions.AppendEventAsync(
                new WorkflowSessionEvent(
                    Guid.NewGuid(),
                    request.WorkflowId,
                    candidate.Id,
                    "candidate_enqueued",
                    "social.workflow.events.candidate_enqueued",
                    null,
                    nowUtc),
                cancellationToken);

            await _queue.EnqueueAsync(
                new ContentWorkflowEnqueueItem(request.WorkflowId, candidate.Id, request.OrganizationId, request.ClientId, request.UserId),
                cancellationToken);

            results.Add(new EnqueuedCandidateResult(requested.CandidateId, true, null));
            totalPieces += requested.Composition.Sum(item => item.Count);
        }

        var estimatedMinutes = WorkflowEtaEstimator.Estimate(
            _etaOptions.AnalysisMinutes,
            _etaOptions.PerPieceMinutes,
            totalPieces);

        // The operator-owned Generator progress view for this exact workflow — NOT the platform-admin
        // content-generation-jobs screen, which redirects a regular org operator away with no visibility
        // into the work they just enqueued.
        var jobsUrl = $"/workflow/progress/{request.WorkflowId}";

        return Result.Success(new EnqueueContentWorkflowCandidatesResponse(
            request.WorkflowId,
            results,
            estimatedMinutes,
            jobsUrl));
    }
}
