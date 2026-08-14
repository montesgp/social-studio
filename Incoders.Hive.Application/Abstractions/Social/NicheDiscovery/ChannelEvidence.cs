namespace Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// Provider-agnostic channel evidence, normalized inside the Infrastructure boundary before crossing
/// into Application (ND-04). Metric fields are <c>long?</c>/<c>int?</c>: <c>null</c> means the network
/// does not report it, <c>0</c> means it reported zero. Never zero-filled (ND-05).
/// </summary>
public sealed record ChannelEvidence(
    string ExternalRef,
    string Handle,
    /// <summary>Provider-reported, opaque, never branched on (design §3).</summary>
    string? Country,
    long? SubscriberCount,
    long? TotalViews,
    int? VideoCount,
    string? LanguageCode,
    DateTimeOffset? CreatedAtUtc,
    string SourceKey);
