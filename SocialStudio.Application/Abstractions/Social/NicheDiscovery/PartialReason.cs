namespace SocialStudio.Application.Abstractions.Social.NicheDiscovery;

/// <summary>Why a <see cref="NicheSourceOutcome.Ran"/> result is partial rather than complete (N9).</summary>
public enum PartialReason
{
    BudgetExhausted = 1,
    ProviderBudgetExhausted = 2,
}
