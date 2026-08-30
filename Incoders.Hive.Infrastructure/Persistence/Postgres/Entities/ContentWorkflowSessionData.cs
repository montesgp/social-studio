namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF data entity for <c>social.content_workflow_sessions</c>. Replaces the prior
/// <c>ConcurrentDictionary</c>-backed <c>InMemoryContentWorkflowService</c> store (design decision #1).
/// Session-level metadata (organization/client/discovery config) lives here — the
/// <see cref="Incoders.Hive.Domain.Social.Workflow.WorkflowSession"/> aggregate intentionally does not
/// carry it.
/// </summary>
public sealed class ContentWorkflowSessionData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ClientId { get; set; }

    public string ClientName { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    /// <summary>Start input snapshot: criterion, strategy mode, networks, origin — jsonb.</summary>
    public string? ConfigJson { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<ContentWorkflowCandidateData> Candidates { get; set; } = [];
}
