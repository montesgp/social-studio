namespace SocialStudio.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// Provider-agnostic video evidence, normalized inside the Infrastructure boundary before crossing
/// into Application (ND-04). Metric fields are nullable: <c>null</c> means the network does not
/// report it, <c>0</c> means it reported zero. Never zero-filled (ND-05).
/// </summary>
public sealed record VideoEvidence(
    string ExternalRef,
    string ChannelExternalRef,
    string Title,
    string? Description,
    DateTimeOffset? PublishedAtUtc,
    long? Views,
    long? Likes,
    long? Comments,
    int? DurationSeconds,
    string? LanguageCode,
    string SourceKey);
