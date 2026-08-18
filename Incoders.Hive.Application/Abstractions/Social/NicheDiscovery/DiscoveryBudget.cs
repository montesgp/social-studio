using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// The per-run ceilings resolved from <see cref="DiscoveryDepth"/> (design §5.3). Threaded through
/// <see cref="IDiscoveryBudgetLedger"/>.
///
/// <see cref="MaxOpportunities"/> is an OUTPUT ceiling, not a cost one — the provider calls have already
/// happened by the time it applies. It exists because a run that returns hundreds of niches returns
/// none: the operator reviews these by hand, and a list nobody can read is the same as no list.
/// Opportunities are ranked by score before it is applied, so the cut keeps the best.
/// </summary>
public sealed record DiscoveryBudget(int ProviderUnits, int AiCompletions, int MaxOpportunities = 40)
{

    public static DiscoveryBudget ForDepth(DiscoveryDepth depth) => depth switch
    {
        DiscoveryDepth.Quick => new DiscoveryBudget(400, 5, MaxOpportunities: 15),
        DiscoveryDepth.Standard => new DiscoveryBudget(1_000, 15, MaxOpportunities: 40),
        DiscoveryDepth.Deep => new DiscoveryBudget(2_500, 40, MaxOpportunities: 100),
        _ => throw new ArgumentOutOfRangeException(nameof(depth), depth, "Unknown discovery depth."),
    };
}
