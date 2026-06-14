using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;

namespace Incoders.Hive.Application.Abstractions.Social.ContentStudio;

/// <summary>
/// Versioned outbound contract carrying an approved content piece to the (future) publication pipeline.
/// Content Studio's responsibility ends when this payload is emitted (PUB-HANDOFF-01/02).
/// </summary>
public sealed record PublicationHandoffPayload(
    int SchemaVersion,
    Guid OrganizationId,
    Guid ClientId,
    Guid ContentItemId,
    ContentOrigin Origin,
    string FormatKey,
    TargetChannel TargetChannel,
    string? Copy,
    IReadOnlyList<ContentAssetRef> Assets,
    string? SchedulingHint,
    DateTime ApprovedAtUtc,
    Guid ApprovedByUserId)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>
/// Outbound port that hands an approved, staged content piece off to publishing.
/// Content Studio MUST NOT schedule or publish — it only emits this payload.
/// </summary>
public interface IPublicationHandoff
{
    Task<Result> HandoffAsync(PublicationHandoffPayload payload, CancellationToken ct);
}
