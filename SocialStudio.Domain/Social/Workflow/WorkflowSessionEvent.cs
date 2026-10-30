namespace SocialStudio.Domain.Social.Workflow;

/// <summary>
/// An append-only audit trail entry for a workflow session or one of its candidates.
/// </summary>
public sealed record WorkflowSessionEvent(
    Guid Id,
    Guid SessionId,
    Guid? CandidateId,
    string Kind,
    string MessageKey,
    string? DataJson,
    DateTime CreatedAtUtc);
