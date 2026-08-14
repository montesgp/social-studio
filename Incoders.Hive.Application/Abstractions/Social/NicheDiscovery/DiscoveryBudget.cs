using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// The provider-unit/AI-completion ceiling for one run, resolved from <see cref="DiscoveryDepth"/>
/// (design §5.3). Threaded through <see cref="IDiscoveryBudgetLedger"/>.
/// </summary>
public sealed record DiscoveryBudget(int ProviderUnits, int AiCompletions)
{
    public static DiscoveryBudget ForDepth(DiscoveryDepth depth) => depth switch
    {
        DiscoveryDepth.Quick => new DiscoveryBudget(400, 5),
        DiscoveryDepth.Standard => new DiscoveryBudget(1_000, 15),
        DiscoveryDepth.Deep => new DiscoveryBudget(2_500, 40),
        _ => throw new ArgumentOutOfRangeException(nameof(depth), depth, "Unknown discovery depth."),
    };
}
