namespace SocialStudio.Application.Abstractions.Social.NicheDiscovery;

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
    /// <summary>Channel avatar, as reported by the provider. Review context: the operator scans a list
    /// of candidate channels by eye before opening any of them.</summary>
    string? ThumbnailUrl,
    /// <summary>Channel description, verbatim from the provider. Says what the channel is ABOUT, which
    /// is what the operator judges a niche by — no metric reads it.</summary>
    string? Description,
    string SourceKey);
