namespace Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// Three outcomes, not two — re-declared here rather than imported from
/// <c>Social.ContentDiscovery</c>, per the vocabulary guard (N2, design §0).
/// </summary>
public enum NicheSourceOutcome
{
    Ran,
    Unconfigured,
    Failed,
}

/// <summary>
/// One source's contribution to one discovery run (design §5.1). <see cref="NicheSourceOutcome.Ran"/>
/// with zero evidence is a SUCCESS. <see cref="NicheSourceOutcome.Unconfigured"/> and
/// <see cref="NicheSourceOutcome.Failed"/> carry empty evidence lists BY CONSTRUCTION — the private
/// constructor plus the three named factories below are what make that structural rather than a
/// convention a caller could violate.
/// </summary>
public sealed record NicheDiscoverySourceResult
{
    private NicheDiscoverySourceResult(
        NicheSourceOutcome outcome,
        IReadOnlyList<ChannelEvidence> channels,
        IReadOnlyList<VideoEvidence> videos,
        BudgetSpend spend,
        PartialReason? partial,
        string? failureReasonKey)
    {
        Outcome = outcome;
        Channels = channels;
        Videos = videos;
        Spend = spend;
        Partial = partial;
        FailureReasonKey = failureReasonKey;
    }

    public NicheSourceOutcome Outcome { get; }

    public IReadOnlyList<ChannelEvidence> Channels { get; }

    public IReadOnlyList<VideoEvidence> Videos { get; }

    public BudgetSpend Spend { get; }

    /// <summary><c>BudgetExhausted</c> | <c>ProviderBudgetExhausted</c> | <c>null</c> (N9).</summary>
    public PartialReason? Partial { get; }

    /// <summary>e.g. <c>"social.niche_discovery.provider_quota_exhausted"</c>. Only set on <see cref="Failed"/>.</summary>
    public string? FailureReasonKey { get; }

    public static NicheDiscoverySourceResult Ran(
        IReadOnlyList<ChannelEvidence> channels,
        IReadOnlyList<VideoEvidence> videos,
        BudgetSpend spend,
        PartialReason? partial = null) =>
        new(NicheSourceOutcome.Ran, channels, videos, spend, partial, null);

    public static NicheDiscoverySourceResult Unconfigured() =>
        new(NicheSourceOutcome.Unconfigured, [], [], BudgetSpend.None, null, null);

    public static NicheDiscoverySourceResult Failed(string failureReasonKey, BudgetSpend? spend = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReasonKey);
        return new(NicheSourceOutcome.Failed, [], [], spend ?? BudgetSpend.None, null, failureReasonKey);
    }
}
