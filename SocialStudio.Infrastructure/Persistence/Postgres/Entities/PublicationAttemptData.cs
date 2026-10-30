namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF Core persistence entity for <c>social.publication_attempts</c> — a one-to-many, cascade-deleted
/// child of <see cref="PublicationData"/>. Unique on <c>(publication_id, attempt_number)</c>.
/// </summary>
public sealed class PublicationAttemptData
{
    public Guid Id { get; set; }

    public Guid PublicationId { get; set; }

    public int AttemptNumber { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    public short Outcome { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ExternalReference { get; set; }

    // Navigation back to the owning publication (FK lives here; configured on the publication side).
    public PublicationData? Publication { get; set; }
}
