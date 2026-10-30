namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>EF data entity for <c>social.content_workflow_events</c>. Append-only audit trail.</summary>
public sealed class ContentWorkflowEventData
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    public Guid? CandidateId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string MessageKey { get; set; } = string.Empty;

    public string? DataJson { get; set; }

    public DateTime CreatedAt { get; set; }
}
