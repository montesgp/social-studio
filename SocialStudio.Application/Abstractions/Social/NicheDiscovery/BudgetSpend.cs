namespace SocialStudio.Application.Abstractions.Social.NicheDiscovery;

/// <summary>What one source call actually spent, reported back so the run response can show it.</summary>
public sealed record BudgetSpend(int ProviderUnits, int AiCompletions)
{
    public static BudgetSpend None { get; } = new(0, 0);
}
