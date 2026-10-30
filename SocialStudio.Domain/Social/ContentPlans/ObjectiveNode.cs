namespace SocialStudio.Domain.Social.ContentPlans;

/// <summary>
/// Immutable Value Object representing the Objective node of a Content Plan.
/// Carries the period objective text, KPI list, and authorship Provenance.
/// Validation limits mirror those migrated from ClientMonthlyObjective.
/// </summary>
public sealed record ObjectiveNode(
    string Objectives,
    IReadOnlyList<string> Kpis,
    Provenance Provenance);
