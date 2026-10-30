using SocialStudio.Domain.Social.NicheDiscovery.Taxonomy;

namespace SocialStudio.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// NAMED SEAM, NOT IMPLEMENTED IN THIS CHANGE (proposal "Content Generator seam", ND-17, design §12).
/// The future activation path consumes opportunities through THIS contract and nothing else. Ships
/// with no implementation and no DI registration in this change — a composition test (WU-11b) asserts
/// <c>GetServices&lt;INicheOpportunityActivationSource&gt;()</c> is empty, so "named, not built" is
/// enforced rather than trusted.
/// </summary>
public interface INicheOpportunityActivationSource
{
    Task<IReadOnlyList<ActivatableContentPattern>> GetActivatablePatternsAsync(
        Guid organizationId, Guid opportunityId, CancellationToken ct = default);
}

public sealed record ActivatableContentPattern(
    ContentPatternKind Pattern,
    IReadOnlyList<string> TopicLabels,
    /// <summary>The local-adaptation hint — a market code, never a country.</summary>
    string TargetMarketCode,
    string SubNicheLabel,
    double OpportunityScore,
    double ConfidenceScore);
