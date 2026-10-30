using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.Clients;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientBrandManuals.GetById;

public sealed class GetClientBrandManualByIdHandler
    : IRequestHandler<GetClientBrandManualByIdQuery, Result<ClientBrandManualResponse>>
{
    private readonly IClientBrandManualRepository _manuals;
    private readonly ISocialClientRepository _clients;

    public GetClientBrandManualByIdHandler(
        IClientBrandManualRepository manuals,
        ISocialClientRepository clients)
    {
        _manuals = manuals;
        _clients = clients;
    }

    public async Task<Result<ClientBrandManualResponse>> Handle(
        GetClientBrandManualByIdQuery request,
        CancellationToken cancellationToken)
    {
        // Defensive client check (ADR 11 / R-A-004)
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<ClientBrandManualResponse>(ClientBrandManualErrors.NotFound);
        }

        var manual = await _manuals.GetByIdAsync(
            new ClientBrandManualId(request.Id), request.OrganizationId, cancellationToken);
        if (manual is null)
        {
            return Result.Failure<ClientBrandManualResponse>(ClientBrandManualErrors.NotFound);
        }

        // Defensive ClientId check — prevents cross-client id-leak (Resolution 1)
        if (manual.ClientId != clientId)
        {
            return Result.Failure<ClientBrandManualResponse>(ClientBrandManualErrors.NotFound);
        }

        return Result.Success(ClientBrandManualResponse.FromDomain(manual));
    }
}
