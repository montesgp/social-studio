namespace SocialStudio.Application.Features.Social.NicheDiscovery.MarketComparison;

/// <summary>
/// <c>TargetMarketGap = Demand × (1 − Competition)</c> (ND-08, design §4). <c>NotReported</c> — modelled
/// as <c>null</c>, never zero-filled (ND-05) — whenever either input is unavailable.
/// </summary>
public static class TargetMarketGap
{
    public static double? Compute(double? demand, double? competition) =>
        demand is null || competition is null ? null : demand.Value * (1 - competition.Value);
}
