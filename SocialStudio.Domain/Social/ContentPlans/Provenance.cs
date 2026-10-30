namespace SocialStudio.Domain.Social.ContentPlans;

/// <summary>
/// Immutable Value Object decorating each generatable node with authorship and status.
/// Provenance is an audit trace ONLY — it MUST NOT block reads, writes, or any downstream operation.
/// EF maps this as an owned type with flat prefixed columns in Infrastructure (no provider types here).
/// </summary>
public sealed record Provenance(
    ProvenanceSource Source,
    ProvenanceStatus Status,
    DateTime? GeneratedAtUtc,
    string? PromptVersion)
{
    /// <summary>
    /// Default provenance for a node that exists structurally but has no content yet.
    /// Source = Manual, Status = Empty.
    /// </summary>
    public static Provenance Empty => new(ProvenanceSource.Manual, ProvenanceStatus.Empty, null, null);

    /// <summary>
    /// Provenance for a node populated by a human. Status = Draft.
    /// </summary>
    public static Provenance Manual(DateTime nowUtc) =>
        new(ProvenanceSource.Manual, ProvenanceStatus.Draft, null, null);
}
