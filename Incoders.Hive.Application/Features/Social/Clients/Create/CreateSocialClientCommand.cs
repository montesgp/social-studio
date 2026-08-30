using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.Create;

public sealed record CreateSocialClientCommand(
    Guid OrganizationId,
    string Name,
    string? Description,
    string? SectorOrNiche = null,
    // WU-8 (BSP-08b): optional policy on creation; null = Unset = legacy 3-tier default.
    BackgroundSourcingPolicy? BackgroundSourcingPolicy = null,
    // WU-7 (CAG-CS-02): optional per-client aesthetic refresh threshold; null = system default (30 days).
    int? AestheticRefreshThresholdDays = null,
    // Issue #70: optional opt-in for @handle signature on generated slides; null/omitted = false
    // (not signed). Plain boolean — no tri-state.
    int? MaxPiecesPerGeneration = null,
    bool SignsOwnSlides = false) : IRequest<Result<SocialClientResponse>>;
