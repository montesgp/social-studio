namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.Scoring;

/// <summary>
/// One opportunity's already-computed, per-signal raw values, ready for set-relative normalization
/// (design §7.1, §3 layer 4 — "computable only from Metrics and Signals"). <c>CrossPlatformValidation</c>
/// deliberately has NO field here: it has no data source in this change and is permanently excluded
/// (N8), never represented as an input a caller could accidentally populate. <c>null</c> on any other
/// field means that signal was unavailable for THIS opportunity — excluded from its score, never zero-filled.
/// </summary>
public sealed record OpportunityInput(
    string ExternalRef,
    double? ViralPerformance,
    double? CrossChannelValidation,
    double? DemandMomentum,
    double? TargetMarketGap,
    double? AutomationSuitability,
    double? EvergreenPotential,
    double? MonetizationPotential);
