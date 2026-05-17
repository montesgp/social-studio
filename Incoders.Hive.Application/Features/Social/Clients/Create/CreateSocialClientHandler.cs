using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.Create;

public sealed class CreateSocialClientHandler
    : IRequestHandler<CreateSocialClientCommand, Result<SocialClientResponse>>
{
    private readonly ISocialClientRepository _clients;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateSocialClientHandler(
        ISocialClientRepository clients,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _clients = clients;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SocialClientResponse>> Handle(
        CreateSocialClientCommand request,
        CancellationToken cancellationToken)
    {
        var created = Client.Create(
            ClientId.New(),
            request.OrganizationId,
            request.Name,
            request.Description,
            _clock.UtcNow);

        if (created.IsFailure)
        {
            return Result.Failure<SocialClientResponse>(created.Error);
        }

        await _clients.AddAsync(created.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SocialClientResponse.FromDomain(created.Value));
    }
}
