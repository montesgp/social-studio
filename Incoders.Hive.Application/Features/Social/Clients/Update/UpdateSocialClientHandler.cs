using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.Update;

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

        _clients.Update(client);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SocialClientResponse.FromDomain(client));
    }
}
