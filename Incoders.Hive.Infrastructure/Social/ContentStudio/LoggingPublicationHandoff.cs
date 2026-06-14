using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Infrastructure.Social.ContentStudio;

/// <summary>
/// Phase 1 publication handoff: records the approved-content handoff (an outbox/log entry) and
/// succeeds, exercising the outbound contract end-to-end without a live publication pipeline.
/// The real consumer (publication pipeline) replaces this adapter in a later change.
/// </summary>
public sealed class LoggingPublicationHandoff : IPublicationHandoff
{
    private readonly ILogger<LoggingPublicationHandoff> _logger;

    public LoggingPublicationHandoff(ILogger<LoggingPublicationHandoff> logger)
    {
        _logger = logger;
    }

    public Task<Result> HandoffAsync(PublicationHandoffPayload payload, CancellationToken ct)
    {
        _logger.LogInformation(
            "Content approved for publication: schema v{SchemaVersion}, org {OrganizationId}, client {ClientId}, " +
            "item {ContentItemId}, format {FormatKey}, channel {Network}/{Placement}, {AssetCount} asset(s).",
            payload.SchemaVersion,
            payload.OrganizationId,
            payload.ClientId,
            payload.ContentItemId,
            payload.FormatKey,
            payload.TargetChannel.Network,
            payload.TargetChannel.Placement,
            payload.Assets.Count);

        return Task.FromResult(Result.Success());
    }
}
