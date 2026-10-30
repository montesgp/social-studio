using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Clients;
using MediatR;

namespace SocialStudio.Application.Features.Social.Clients.Update;

public sealed class UpdateSocialClientHandler
    : IRequestHandler<UpdateSocialClientCommand, Result<SocialClientResponse>>
{
    private readonly ISocialClientRepository _clients;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpdateSocialClientHandler(
        ISocialClientRepository clients,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _clients = clients;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SocialClientResponse>> Handle(
        UpdateSocialClientCommand request,
        CancellationToken cancellationToken)
    {
        var client = await _clients.GetByIdAsync(
            new ClientId(request.Id),
            request.OrganizationId,
            cancellationToken);

        if (client is null)
        {
            return Result.Failure<SocialClientResponse>(ClientErrors.NotFound);
        }

        var renamed = client.Rename(request.Name, request.Description, _clock.UtcNow, request.SectorOrNiche);
        if (renamed.IsFailure)
        {
            return Result.Failure<SocialClientResponse>(renamed.Error);
        }

        // WU-8 (BSP-08b): update policy when provided in the request.
        // A null request.BackgroundSourcingPolicy means "leave the current policy unchanged",
        // not "clear the policy". Use SetBackgroundSourcingPolicy(null, ...) explicitly to clear.
        if (request.BackgroundSourcingPolicy.HasValue)
        {
            client.SetBackgroundSourcingPolicy(request.BackgroundSourcingPolicy, _clock.UtcNow);
        }

        // WU-7 (CAG-CS-02): update threshold when provided; null = leave current unchanged.
        if (request.AestheticRefreshThresholdDays.HasValue)
        {
            var thresholdResult = client.SetAestheticRefreshThresholdDays(
                request.AestheticRefreshThresholdDays, _clock.UtcNow);
            if (thresholdResult.IsFailure)
            {
                return Result.Failure<SocialClientResponse>(thresholdResult.Error);
            }
        }

        // Per-client generation piece cap; null = leave current unchanged (same convention as the
        // threshold above).
        if (request.MaxPiecesPerGeneration.HasValue)
        {
            var capResult = client.SetMaxPiecesPerGeneration(
                request.MaxPiecesPerGeneration, _clock.UtcNow);
            if (capResult.IsFailure)
            {
                return Result.Failure<SocialClientResponse>(capResult.Error);
            }
        }

        // Issue #70: always-explicit opt-in for @handle signature — plain boolean, every update
        // sets the flag (no "leave unchanged" sentinel, unlike the nullable-override fields above).
        client.SetSignsOwnSlides(request.SignsOwnSlides, _clock.UtcNow);

        _clients.Update(client);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SocialClientResponse.FromDomain(client));
    }
}
