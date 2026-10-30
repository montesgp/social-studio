using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.Clients;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientBrandManuals.Deactivate;

public sealed class DeactivateClientBrandManualHandler
    : IRequestHandler<DeactivateClientBrandManualCommand, Result<ClientBrandManualResponse>>
{
    private readonly IClientBrandManualRepository _manuals;
    private readonly IClock _clock;

    public DeactivateClientBrandManualHandler(
        IClientBrandManualRepository manuals,
        IClock clock)
    {
        _manuals = manuals;
        _clock = clock;
    }

    public async Task<Result<ClientBrandManualResponse>> Handle(
        DeactivateClientBrandManualCommand request,
        CancellationToken cancellationToken)
    {
        var manual = await _manuals.GetByIdAsync(
            new ClientBrandManualId(request.Id), request.OrganizationId, cancellationToken);
        if (manual is null)
        {
            return Result.Failure<ClientBrandManualResponse>(ClientBrandManualErrors.NotFound);
        }

        // Defensive ClientId check (ADR 11 tightening)
        if (manual.ClientId != new ClientId(request.ClientId))
        {
            return Result.Failure<ClientBrandManualResponse>(ClientBrandManualErrors.NotFound);
        }

        var result = manual.Deactivate(_clock.UtcNow);
        if (result.IsFailure)
        {
            return Result.Failure<ClientBrandManualResponse>(result.Error);
        }

        _manuals.Update(manual);
        await _manuals.SaveChangesAsync(cancellationToken);

        return Result.Success(ClientBrandManualResponse.FromDomain(manual));
    }
}
