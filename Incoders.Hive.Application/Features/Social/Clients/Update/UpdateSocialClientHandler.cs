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

        var renamed = client.Rename(request.Name, request.Description, _clock.UtcNow);
        if (renamed.IsFailure)
        {
            return Result.Failure<SocialClientResponse>(renamed.Error);
        }

        _clients.Update(client);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SocialClientResponse.FromDomain(client));
    }
}
