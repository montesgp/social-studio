using SocialStudio.Domain.Social.Workflow;

namespace SocialStudio.Application.Abstractions.Social;

/// <summary>Read model for one row of the client-scoped workflow history list.</summary>
public sealed record WorkflowSessionSummary(
    Guid Id,
    Guid ClientId,
    string ClientName,
    string Status,
    string? ConfigJson,
    int TotalResults,
    int SelectedCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>One persisted candidate row, as read back for the discovery status/results screens
/// (Phase 5.9 facade). Distinct from <see cref="SocialStudio.Domain.Social.Workflow.WorkflowCandidate"/>:
/// that aggregate owns only enqueue-sequencing identity, while this DTO carries the full discovery
/// payload snapshot the UI renders.</summary>
public sealed record WorkflowCandidateDetail(
    Guid Id,
    string ExternalResultId,
    string? PayloadJson,
    bool IsSelected,
    string Status,
    string? FailureCode = null);

/// <summary>Full session row plus its candidates' discovery payloads — the read shape
/// <see cref="SocialStudio.Domain.Social.Workflow.WorkflowSession"/> intentionally does not carry
/// (session metadata is a persistence concern; see <see cref="InsertAsync"/>).</summary>
public sealed record WorkflowSessionDetail(
    Guid Id,
    Guid OrganizationId,
    Guid ClientId,
    string ClientName,
    string Status,
    string? ConfigJson,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<WorkflowCandidateDetail> Candidates);

/// <summary>One discovered candidate awaiting persistence, appended to an already-inserted session by
/// the background discovery task (Phase 5.9).</summary>
public sealed record WorkflowDiscoveredCandidate(
    Guid Id,
    string ExternalResultId,
    string PayloadJson);

/// <summary>
/// Persistence port for workflow sessions/candidates/events. Normalized Postgres tables behind this
/// port replace the prior <c>ConcurrentDictionary</c>-backed in-memory service.
/// </summary>
public interface IContentWorkflowSessionRepository
{
    /// <summary>
    /// Persists a brand-new session row plus any candidates already attached to
    /// <paramref name="session"/> (normally none — candidates are appended later via discovery).
    /// Session-level metadata is carried as explicit parameters, not on <see cref="WorkflowSession"/>
    /// itself: the aggregate intentionally owns only candidate identity (design: "session-level
    /// metadata ... is a persistence concern"), the same reasoning already applied to
    /// <c>EnqueueContentWorkflowCandidatesCommand.ClientId</c>.
    /// </summary>
    Task InsertAsync(
        WorkflowSession session,
        Guid organizationId,
        Guid clientId,
        string clientName,
        string status,
        string? configJson,
        CancellationToken cancellationToken = default);

    Task<WorkflowSession?> GetByIdAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Worker-only lookup: bypasses the organization scope check because the background
    /// worker already trusts the persisted <c>organization_id</c> on the queue item.</summary>
    Task<WorkflowSession?> GetByIdInternalAsync(Guid id, CancellationToken cancellationToken = default);

    Task ReplaceSelectionAsync(
        Guid sessionId,
        IReadOnlyList<Guid> candidateIds,
        CancellationToken cancellationToken = default);

    Task SetCandidateCompositionAsync(
        Guid candidateId,
        string compositionJson,
        string enqueueKey,
        CancellationToken cancellationToken = default);

    Task UpdateCandidateStatusAsync(
        Guid candidateId,
        WorkflowCandidateStatus status,
        Guid? analysisId,
        Guid? batchId,
        string? failureCode,
        CancellationToken cancellationToken = default);

    Task AppendEventAsync(WorkflowSessionEvent workflowEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowSessionSummary>> ListAsync(
        Guid organizationId,
        Guid? clientId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Appends discovered candidates to an already-inserted session. Called once by the
    /// background discovery task after the scrape/idea-draw finishes (Phase 5.9 facade); the session
    /// itself was already persisted empty by <see cref="InsertAsync"/> so the operator's poll has
    /// something to find immediately.</summary>
    Task AddCandidatesAsync(
        Guid sessionId,
        IReadOnlyList<WorkflowDiscoveredCandidate> candidates,
        CancellationToken cancellationToken = default);

    /// <summary>Persists the session-level discovery outcome (status + updated config snapshot) once the
    /// background task reaches a terminal phase.</summary>
    Task UpdateSessionStateAsync(
        Guid sessionId,
        string status,
        string? configJson,
        CancellationToken cancellationToken = default);

    /// <summary>Org-scoped read of the full session row plus candidate payloads, for the status/results
    /// screens.</summary>
    Task<WorkflowSessionDetail?> GetDetailAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Worker-only counterpart of <see cref="GetDetailAsync"/> — bypasses organization scope,
    /// same reasoning as <see cref="GetByIdInternalAsync"/>.</summary>
    Task<WorkflowSessionDetail?> GetDetailInternalAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
