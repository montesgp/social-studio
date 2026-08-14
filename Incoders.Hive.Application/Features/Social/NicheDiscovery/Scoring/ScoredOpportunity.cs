namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.Scoring;

/// <summary>
/// A signal excluded from one opportunity's <see cref="ScoredOpportunity.OpportunityScore"/> — either
/// permanently (<c>cross_platform_validation</c>, N8) or because it was unavailable for that particular
/// opportunity. Surfaced so the operator sees WHY the effective denominator changed, never inferred.
/// </summary>
public sealed record ExcludedSignal(string Key, string ReasonKey, string I18nKey);

/// <summary>Output of <see cref="NicheOpportunityScorer.Score"/> — design §3 layer 4 (SCORE).</summary>
public sealed record ScoredOpportunity(
    string ExternalRef,
    double OpportunityScore,
    double EffectiveWeightDenominator,
    IReadOnlyList<ExcludedSignal> ExcludedSignals);
