using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Abstractions.Social;

public interface ISocialContentExtractor
{
    Task<Result<SocialContentSnapshot>> ExtractAsync(
        SocialContentRequest request,
        CancellationToken cancellationToken = default);
}

public interface ISocialContentResolver
{
    Task<Result<SocialContentResolution>> ResolveAsync(
        SocialContentResolutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record SocialContentRequest(
    Guid OrganizationId,
    string SourceUrl,
    string NormalizedUrl,
    string PlatformKey,
    string CapabilityKey);

public sealed record SocialContentResolutionRequest(
    Guid OrganizationId,
    Guid? ClientId,
    string SourceUrl,
    string? PlatformHint,
    string? PreferredProviderKey);

public sealed record SocialContentResolution(
    SocialContentSnapshot Snapshot,
    IReadOnlyList<ProviderFailureDiagnostic> Diagnostics);

public sealed record SocialContentSnapshot(
    string PlatformKey,
    string CanonicalUrl,
    string? Title,
    string? Description,
    string? Author,
    string? ThumbnailUrl,
    int? DurationSeconds,
    IReadOnlyDictionary<string, long> Metrics,
    string? Transcript,
    IReadOnlyList<SocialMediaReference> MediaReferences,
    string? RawPayloadReference,
    IReadOnlyList<SocialContentWarning> Warnings);

public sealed record SocialMediaReference(
    string Kind,
    string Uri,
    string? MimeType);

public sealed record SocialContentWarning(
    string Code,
    string MessageKey);