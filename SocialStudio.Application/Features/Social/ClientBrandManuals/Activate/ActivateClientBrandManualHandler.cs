using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.Clients;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientBrandManuals.Activate;

public sealed class ActivateClientBrandManualHandler
    : IRequestHandler<ActivateClientBrandManualCommand, Result<ClientBrandManualResponse>>
{
    private readonly IClientBrandManualRepository _manuals;
    private readonly IClock _clock;

    public ActivateClientBrandManualHandler(
        IClientBrandManualRepository manuals,
        IClock clock)
    {
        _manuals = manuals;
        _clock = clock;
    }

    public async Task<Result<ClientBrandManualResponse>> Handle(
        ActivateClientBrandManualCommand request,
        CancellationToken cancellationToken)
    {
        var manual = await _manuals.GetByIdAsync(
            new ClientBrandManualId(request.Id), request.OrganizationId, cancellationToken);
        if (manual is null)
        {
            return Result.Failure<ClientBrandManualResponse>(ClientBrandManualErrors.NotFound);
        }

        // Defensive ClientId check (ADR 11)
        if (manual.ClientId != new ClientId(request.ClientId))
        {
            return Result.Failure<ClientBrandManualResponse>(ClientBrandManualErrors.NotFound);
        }

        // Conflict pre-check (ADR 8 — Activate refuses if another manual is already active)
        var activeManual = await _manuals.GetActiveByClientAsync(manual.ClientId, request.OrganizationId, cancellationToken);
        if (activeManual is not null && activeManual.Id != manual.Id)
        {
            return Result.Failure<ClientBrandManualResponse>(ClientBrandManualErrors.AnotherManualActive);
        }

        var result = manual.Activate(_clock.UtcNow);
        if (result.IsFailure)
        {
            return Result.Failure<ClientBrandManualResponse>(result.Error);
        }

        _manuals.Update(manual);
        await _manuals.SaveChangesAsync(cancellationToken);

        return Result.Success(ClientBrandManualResponse.FromDomain(manual));
    }
}
