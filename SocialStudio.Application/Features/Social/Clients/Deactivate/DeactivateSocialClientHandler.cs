using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Clients;
using MediatR;

namespace SocialStudio.Application.Features.Social.Clients.Deactivate;

public sealed class DeactivateSocialClientHandler
    : IRequestHandler<DeactivateSocialClientCommand, Result<SocialClientResponse>>
{
    private readonly ISocialClientRepository _clients;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public DeactivateSocialClientHandler(
        ISocialClientRepository clients,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _clients = clients;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SocialClientResponse>> Handle(
        DeactivateSocialClientCommand request,
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

        var deactivated = client.Deactivate(_clock.UtcNow);
        if (deactivated.IsFailure)
        {
            return Result.Failure<SocialClientResponse>(deactivated.Error);
        }

        _clients.Update(client);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SocialClientResponse.FromDomain(client));
    }
}
