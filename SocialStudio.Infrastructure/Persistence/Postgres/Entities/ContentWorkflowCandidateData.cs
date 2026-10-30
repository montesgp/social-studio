namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF data entity for <c>social.content_workflow_candidates</c>. One row per discovered candidate
/// within a session; <see cref="EnqueueKey"/> backs the partial unique index that enforces "double-click
/// does not double-bill" at the DB layer (defense in depth — the Application handler already guards
/// idempotent re-enqueue, see <c>EnqueueContentWorkflowCandidatesCommandHandler</c>).
/// </summary>
public sealed class ContentWorkflowCandidateData
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    public string ExternalResultId { get; set; } = string.Empty;

    /// <summary>Discovery result payload snapshot (title, url, thumbnail, metrics, summary, network) — jsonb.</summary>
    public string? PayloadJson { get; set; }

    public bool IsSelected { get; set; }

    /// <summary>Operator-chosen <c>[{formatKey,count}]</c> — jsonb. Empty array until composed.</summary>
    public string CompositionJson { get; set; } = "[]";

    /// <summary>Null until composed — <see cref="WorkflowCandidateEnqueueKey.Create"/> output.</summary>
    public string? EnqueueKey { get; set; }

    /// <summary><see cref="SocialStudio.Domain.Social.Workflow.WorkflowCandidateStatus"/> stored by name.</summary>
    public string Status { get; set; } = string.Empty;

    public string? FailureCode { get; set; }

    public Guid? AnalysisId { get; set; }

    public Guid? BatchId { get; set; }

    public DateTime QueuedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ContentWorkflowSessionData? Session { get; set; }
}
