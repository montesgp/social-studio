using SocialStudio.Application.Features.Social.ContentGeneration.Batches;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Workflow.Enqueue;

/// <summary>One candidate's operator-chosen format composition, submitted for enqueue.</summary>
public sealed record WorkflowCandidateComposition(
    Guid CandidateId,
    IReadOnlyList<BatchCompositionItem> Composition);

/// <summary>
/// Enqueues N candidates in one operator action. Each candidate's analysis-then-batch sequence starts
/// independently — a rejected or later-failing candidate never blocks or unwinds its siblings.
/// </summary>
/// <param name="ClientId">
/// Carried explicitly rather than re-derived from the persisted session: <see cref="Domain.Social.Workflow.WorkflowSession"/>
/// is a pure candidate-identity aggregate and does not hold session-level metadata (design: "session-level
/// metadata ... is a persistence concern"). The operator's screen already has the client in context for
/// the whole workflow run, so the caller supplies it.
/// </param>
public sealed record EnqueueContentWorkflowCandidatesCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid UserId,
    Guid WorkflowId,
    IReadOnlyList<WorkflowCandidateComposition> Candidates) : IRequest<Result<EnqueueContentWorkflowCandidatesResponse>>;

/// <summary>Per-candidate outcome of one enqueue request. Rejected candidates carry the reason so the
/// operator can fix and resubmit just that one.</summary>
public sealed record EnqueuedCandidateResult(
    Guid CandidateId,
    bool Accepted,
    string? RejectionReason);

public sealed record EnqueueContentWorkflowCandidatesResponse(
    Guid WorkflowId,
    IReadOnlyList<EnqueuedCandidateResult> Candidates,
    int EstimatedMinutes,
    string JobsUrl);
