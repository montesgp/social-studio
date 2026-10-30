namespace SocialStudio.Domain.Social.ContentPlans;

/// <summary>
/// Immutable Value Object representing the per-period Content Strategy node.
/// Distinct from GeneralStrategy — this is the period-scoped, manually/AI-fillable strategy.
/// StrategyText is optional; the node may be empty (Provenance.Status = Empty) on initial plan creation.
/// </summary>
public sealed record ContentStrategyNode(
    string? StrategyText,
    Provenance Provenance);
