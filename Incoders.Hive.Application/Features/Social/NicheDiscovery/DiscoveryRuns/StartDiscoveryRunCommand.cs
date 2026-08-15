using Incoders.Hive.Application.Features.Social.NicheDiscovery.Scoring;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>
/// Starts a discovery run (design §10, ND-01): validates market/window/depth/format/network, persists
/// the run in <c>Requested</c> status with an immutable configuration snapshot, and enqueues it for the
/// background processor. Returns immediately so the caller can poll (start-and-poll idiom).
/// </summary>
public sealed record StartDiscoveryRunCommand(
    Guid OrganizationId,
    Guid UserId,
    string SourceMarketCode,
    string TargetMarketCode,
    string NetworkKey,
    string ContentFormat,
    int? WindowDays,
    string? Depth,
    Guid? NicheId,
    IReadOnlyList<string> SeedTerms,
    OpportunityScoreWeights? Weights) : IRequest<Result<StartDiscoveryRunResponse>>;
