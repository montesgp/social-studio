namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Immutable snapshot of a client's current monthly objective, captured at job-start time.
/// </summary>
public sealed record MonthlyObjectiveSnapshot(
    string Objectives,
    IReadOnlyList<string> Kpis);
