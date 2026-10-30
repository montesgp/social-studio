namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

public sealed class ProviderCostRateData
{
    public Guid Id { get; set; }

    public string ProviderKey { get; set; } = string.Empty;

    public string? Model { get; set; }

    public decimal? CostPer1KInputTokens { get; set; }

    public decimal? CostPer1KOutputTokens { get; set; }

    public decimal? CostPerRequest { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public string? Notes { get; set; }
}
