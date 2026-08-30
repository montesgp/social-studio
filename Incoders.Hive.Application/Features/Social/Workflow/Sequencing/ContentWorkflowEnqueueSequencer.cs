using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Batches;
using Incoders.Hive.Application.Features.Social.SingleAnalyzer.Analyze;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Incoders.Hive.Domain.Social.Workflow;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Application.Features.Social.Workflow.Sequencing;

/// <summary>
/// Runs ONE candidate's independent analysis-then-batch sequence (design "Analysis→batch sequencing",
/// decision #3/#4). Consumed by <c>ContentWorkflowEnqueueBackgroundWorker</c> once per
/// <see cref="ContentWorkflowEnqueueItem"/> dequeued — one call here is one candidate; sibling isolation
/// falls out of the fan-out (each queue item gets its own call, its own try/catch, its own terminal
/// status write). Testable independent of the hosted worker: no <c>BackgroundService</c>/DI-scope
/// concerns here, only <see cref="ISender"/> + the two ports it already depends on.
/// </summary>
public sealed class ContentWorkflowEnqueueSequencer
{
    /// <summary>How often the bounded wait re-checks the analysis status. Not user-configurable —
    /// only the overall <see cref="ContentWorkflowEnqueueOptions.AnalysisWaitTimeout"/> budget is.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly ISender _sender;
    private readonly IContentWorkflowSessionRepository _sessions;
    private readonly ISingleContentAnalysisRepository _analyses;
    private readonly IClock _clock;
    private readonly IAsyncDelay _delay;
    private readonly ContentWorkflowEnqueueOptions _options;
    private readonly ILogger<ContentWorkflowEnqueueSequencer> _logger;

    public ContentWorkflowEnqueueSequencer(
        ISender sender,
        IContentWorkflowSessionRepository sessions,
        ISingleContentAnalysisRepository analyses,
        IClock clock,
        IAsyncDelay delay,
        ContentWorkflowEnqueueOptions options,
        ILogger<ContentWorkflowEnqueueSequencer> logger)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(analyses);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _sender = sender;
        _sessions = sessions;
        _analyses = analyses;
        _clock = clock;
        _delay = delay;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Runs the full sequence for one candidate. Never throws for expected/business failures — those
    /// are recorded as the candidate's terminal status. Only unexpected exceptions propagate, so the
    /// caller (hosted worker) can log+swallow per item without losing sibling processing.
    /// </summary>
    public async Task RunAsync(ContentWorkflowEnqueueItem item, CancellationToken cancellationToken)
    {
        var session = await _sessions.GetByIdInternalAsync(item.SessionId, cancellationToken);
        var candidate = session?.Candidates.FirstOrDefault(c => c.Id == item.CandidateId);

        if (candidate is null)
        {
            _logger.LogWarning(
                "ContentWorkflow sequencer: candidate {CandidateId} not found in session {SessionId} — skipping.",
                item.CandidateId, item.SessionId);
            return;
        }

        await _sessions.UpdateCandidateStatusAsync(
            item.CandidateId, WorkflowCandidateStatus.Analyzing, null, null, null, cancellationToken);

        var analyzeResult = await _sender.Send(
            new AnalyzeSingleContentCommand(
                item.OrganizationId, item.UserId, candidate.ExternalResultId, item.ClientId, null),
            cancellationToken);

        if (analyzeResult.IsFailure)
        {
            await FailAnalysisAsync(item, analyzeResult.Error.Code, cancellationToken);
            return;
        }

        var analysisId = analyzeResult.Value.Id;
        var waitResult = await WaitForAnalysisAsync(analysisId, item.OrganizationId, cancellationToken);

        if (!waitResult.Succeeded)
        {
            await FailAnalysisAsync(item, waitResult.FailureCode!, cancellationToken, analysisId);
            return;
        }

        await _sessions.UpdateCandidateStatusAsync(
            item.CandidateId, WorkflowCandidateStatus.BatchQueued, analysisId, null, null, cancellationToken);

        var composition = candidate.Composition
            .Select(entry => new BatchCompositionItem(entry.FormatKey, entry.Count))
            .ToList();

        var batchResult = await _sender.Send(
            new CreateContentGenerationBatchCommand(
                item.OrganizationId, item.ClientId, analysisId, composition, null, null),
            cancellationToken);

        if (batchResult.IsFailure)
        {
            await _sessions.UpdateCandidateStatusAsync(
                item.CandidateId, WorkflowCandidateStatus.BatchFailed, analysisId, null, batchResult.Error.Code,
                cancellationToken);
            return;
        }

        await _sessions.UpdateCandidateStatusAsync(
            item.CandidateId, WorkflowCandidateStatus.BatchStarted, analysisId, batchResult.Value.BatchId, null,
            cancellationToken);
    }

    private async Task<AnalysisWaitOutcome> WaitForAnalysisAsync(
        Guid analysisId, Guid organizationId, CancellationToken cancellationToken)
    {
        var startedAtUtc = _clock.UtcNow;

        while (true)
        {
            var analysis = await _analyses.GetByIdAsync(analysisId, organizationId, cancellationToken);

            if (analysis is null)
            {
                return AnalysisWaitOutcome.Failed(WorkflowCandidateErrors.CandidateVanished.Code);
            }

            if (analysis.Status == SingleContentAnalysisStatus.Analyzed)
            {
                return AnalysisWaitOutcome.Success;
            }

            if (analysis.Status == SingleContentAnalysisStatus.Failed)
            {
                return AnalysisWaitOutcome.Failed(
                    analysis.FailureCode ?? WorkflowCandidateErrors.AnalysisTimedOut.Code);
            }

            if (_clock.UtcNow - startedAtUtc >= _options.AnalysisWaitTimeout)
            {
                return AnalysisWaitOutcome.Failed(WorkflowCandidateErrors.AnalysisTimedOut.Code);
            }

            await _delay.DelayAsync(PollInterval, cancellationToken);
        }
    }

    private Task FailAnalysisAsync(
        ContentWorkflowEnqueueItem item, string failureCode, CancellationToken cancellationToken, Guid? analysisId = null) =>
        _sessions.UpdateCandidateStatusAsync(
            item.CandidateId, WorkflowCandidateStatus.AnalysisFailed, analysisId, null, failureCode, cancellationToken);

    private readonly record struct AnalysisWaitOutcome(bool Succeeded, string? FailureCode)
    {
        public static readonly AnalysisWaitOutcome Success = new(true, null);

        public static AnalysisWaitOutcome Failed(string failureCode) => new(false, failureCode);
    }
}
