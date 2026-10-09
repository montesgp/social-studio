using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.Workflow;

/// <summary>
/// One candidate composition within a <see cref="WorkflowSession"/>. Pure aggregate invariants only —
/// persistence payload (title, url, metrics, etc.) is an Infrastructure/Application concern.
/// </summary>
public sealed class WorkflowCandidate
{
    private readonly List<(string FormatKey, int Count)> _composition = [];

    private WorkflowCandidate(Guid id, string externalResultId, string? sourceUrl)
    {
        Id = id;
        ExternalResultId = externalResultId;
        SourceUrl = sourceUrl;
        Status = WorkflowCandidateStatus.Discovered;
    }

    public Guid Id { get; }

    public string ExternalResultId { get; }

    /// <summary>The real, resolvable content URL this candidate was discovered from (e.g. the TikTok/
    /// Instagram/YouTube permalink) — what <c>AnalyzeSingleContentCommand</c> actually scrapes.
    /// Distinct from <see cref="ExternalResultId"/>, which is only a discovery-time identity token and
    /// is NOT guaranteed to be a URL. <c>null</c>/empty means discovery persisted no usable URL for this
    /// candidate (e.g. malformed payload) — callers must fail cleanly rather than analyze an empty URL.</summary>
    public string? SourceUrl { get; }

    public WorkflowCandidateStatus Status { get; private set; }

    public IReadOnlyList<(string FormatKey, int Count)> Composition => _composition;

    public static WorkflowCandidate Create(Guid id, string externalResultId, string? sourceUrl = null) =>
        new(id, externalResultId, sourceUrl);

    /// <summary>
    /// Reconstructs a candidate from an already-persisted row. Skips the invariants <see cref="Create"/>
    /// enforces at write-time (empty composition, transition table) because a persisted row already
    /// satisfied them when it was originally written — re-validating on every read would reject a
    /// candidate still in its pre-composition <see cref="WorkflowCandidateStatus.Discovered"/> or
    /// <see cref="WorkflowCandidateStatus.Queued"/> state.
    /// Repository mapping only; Application code must use <see cref="Create"/> + <see cref="SetComposition"/>
    /// + <see cref="MoveTo"/>.
    /// </summary>
    public static WorkflowCandidate Hydrate(
        Guid id,
        string externalResultId,
        string? sourceUrl,
        WorkflowCandidateStatus status,
        IReadOnlyList<(string FormatKey, int Count)> composition)
    {
        var candidate = new WorkflowCandidate(id, externalResultId, sourceUrl) { Status = status };
        if (composition.Count > 0)
        {
            candidate._composition.AddRange(composition);
        }

        return candidate;
    }

    /// <summary>Empty composition is rejected at the domain level — enqueue can never target zero formats.</summary>
    public Result SetComposition(IReadOnlyList<(string FormatKey, int Count)> composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        if (composition.Count == 0)
        {
            return Result.Failure(WorkflowCandidateErrors.EmptyComposition);
        }

        _composition.Clear();
        _composition.AddRange(composition);
        return Result.Success();
    }

    public Result MoveTo(WorkflowCandidateStatus status)
    {
        if (!WorkflowCandidateTransition.CanMoveTo(Status, status))
        {
            return Result.Failure(WorkflowCandidateErrors.InvalidTransition);
        }

        Status = status;
        return Result.Success();
    }
}
