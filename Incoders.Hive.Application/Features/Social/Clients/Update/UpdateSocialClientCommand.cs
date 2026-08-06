using Incoders.Hive.Domain.Common;
using Incoders.Hive.Application.Features.Social.Clients;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.Update;

public sealed record UpdateSocialClientCommand(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string? Description,
    string? SectorOrNiche = null,
    // WU-8 (BSP-08b): optional policy update; null = leave policy unchanged if already set,
    // or keeps null/Unset for legacy default. Handler calls SetBackgroundSourcingPolicy when non-null.
    BackgroundSourcingPolicy? BackgroundSourcingPolicy = null,
    // WU-7 (CAG-CS-02): optional threshold update; null = leave current threshold unchanged.
    // To explicitly clear to null (system default), the caller must use a sentinel or a dedicated
    // clear endpoint; for now null means "no change" (mirrors policy pattern).
    int? AestheticRefreshThresholdDays = null,
    // Issue #70: always-explicit opt-in for @handle signature on generated slides. Plain boolean —
    // no tri-state, no "leave unchanged" sentinel; every update sets the flag explicitly.
    bool SignsOwnSlides = false,
    // Per-client generation piece cap. Null = leave the current cap unchanged (mirrors the
    // threshold pattern above); clearing back to "no cap" needs an explicit sentinel.
    int? MaxPiecesPerGeneration = null) : IRequest<Result<SocialClientResponse>>;
