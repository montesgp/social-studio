using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.Workflow;

/// <summary>
/// Aggregate root for one discovery-to-enqueue workflow run. Owns candidate identity invariants;
/// session-level metadata (organization, client, discovery config) is a persistence concern.
/// </summary>
public sealed class WorkflowSession
{
    private readonly List<WorkflowCandidate> _candidates = [];

    private WorkflowSession(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; }

    public IReadOnlyList<WorkflowCandidate> Candidates => _candidates;

    public static WorkflowSession Create(Guid id) => new(id);

    /// <summary>Duplicate candidate ids are rejected at the domain level — a candidate belongs to one
    /// session exactly once.</summary>
    public Result AddCandidate(WorkflowCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (_candidates.Any(existing => existing.Id == candidate.Id))
        {
            return Result.Failure(WorkflowCandidateErrors.DuplicateCandidate);
        }

        _candidates.Add(candidate);
        return Result.Success();
    }
}
