namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.Scoring;

/// <summary>
/// The full weight table for <see cref="NicheOpportunityScorer"/> (design §7.1). Snapshot-per-run
/// shape (N7): the effective set used by a run is captured into <c>discovery_runs.weights_json</c> at
/// run start, so a later change to <see cref="Default"/> never rewrites the meaning of a past run.
/// </summary>
public sealed record OpportunityScoreWeights(
    double ViralPerformance,
    double CrossChannelValidation,
    double DemandMomentum,
    double TargetMarketGap,
    double AutomationSuitability,
    double EvergreenPotential,
    double CrossPlatformValidation,
    double MonetizationPotential)
{
    public static readonly OpportunityScoreWeights Default = new(
        ViralPerformance: 0.20,
        CrossChannelValidation: 0.15,
        DemandMomentum: 0.15,
        TargetMarketGap: 0.15,
        AutomationSuitability: 0.10,
        EvergreenPotential: 0.10,
        CrossPlatformValidation: 0.10,
        MonetizationPotential: 0.05);
}
