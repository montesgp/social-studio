namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF data entity for <c>social.niche_opportunities</c> — append-only read model (design §9, ND-11).
/// <see cref="NicheId"/> is a deliberate value cross-reference to <c>Domain.Social.Niches</c>, NEVER an
/// FK — the catalogs are parallel (N5/settled decision 2). Nullable metric columns: <c>null</c> means the
/// underlying signal was unavailable for this opportunity, <c>0</c> means it was computed as zero. Never
/// zero-filled.
/// </summary>
public sealed class NicheOpportunityData
{
    public Guid Id { get; set; }

    public Guid RunId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid? NicheId { get; set; }

    public string? SubNicheLabel { get; set; }

    public string ContentPattern { get; set; } = string.Empty;

    public List<string> TopicLabels { get; set; } = [];

    public double OpportunityScore { get; set; }

    public double ConfidenceScore { get; set; }

    /// <summary>Sum of the weights of the signals that were AVAILABLE for this opportunity (design §7.1)
    /// — surfaced so the operator sees exactly how much of the weight table the score is based on.</summary>
    public double EffectiveWeightDenominator { get; set; }

    /// <summary>Serialized <c>ExcludedSignal[]</c> (design §7.1) — always includes
    /// <c>cross_platform_validation</c> in this change, since it has no data source yet (N8).</summary>
    public string ExcludedSignalsJson { get; set; } = string.Empty;

    public long? MedianViews { get; set; }

    public double? OutlierRatio { get; set; }

    public double? OutlierDensity { get; set; }

    public double? GrowthProxy { get; set; }

    public int? CrossChannelRepetition { get; set; }

    public double? TargetMarketDemand { get; set; }

    public double? TargetMarketCompetition { get; set; }

    public double? TargetMarketGap { get; set; }

    /// <summary>Target-market demand before run-normalization: the only value with absolute magnitude.</summary>
    public double? TargetMarketRawDemand { get; set; }

    public double? AutomationSuitability { get; set; }

    public int ChannelSampleSize { get; set; }

    public int VideoSampleSize { get; set; }

    public int Rank { get; set; }

    public DateTime ObservedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
