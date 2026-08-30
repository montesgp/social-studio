namespace Incoders.Hive.Domain.Social.ContentStudio;

/// <summary>
/// Audit-only trace of the most recent draft change (who/when, and the prompt version when an AI
/// edit produced the current draft). Never blocks a transition — same discipline as content-plan Provenance.
/// </summary>
public sealed record EditTrace(
    EditSource LastEditedSource,
    DateTime? LastEditedAtUtc,
    string? PromptVersion)
{
    /// <summary>Initial trace for a freshly materialized item: manual authorship, no edit yet.</summary>
    public static EditTrace Initial() => new(EditSource.Manual, null, null);

    /// <summary>Trace after a human edit at <paramref name="nowUtc"/>.</summary>
    public static EditTrace ManualEdit(DateTime nowUtc) => new(EditSource.Manual, nowUtc, null);
}
