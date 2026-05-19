using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientBrandManuals.GetActive;

public sealed class GetActiveClientBrandManualHandler
    : IRequestHandler<GetActiveClientBrandManualQuery, Result<ClientBrandManualResponse>>
{
    private readonly IClientBrandManualRepository _manuals;
    private readonly ISocialClientRepository _clients;

    public GetActiveClientBrandManualHandler(
        IClientBrandManualRepository manuals,
        ISocialClientRepository clients)
    {
        _manuals = manuals;
        _clients = clients;
    }

    public async Task<Result<ClientBrandManualResponse>> Handle(
        GetActiveClientBrandManualQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<ClientBrandManualResponse>(ClientBrandManualErrors.NotFound);
        }

        var manual = await _manuals.GetActiveByClientAsync(clientId, request.OrganizationId, cancellationToken);
        if (manual is null)
        {
            return Result.Failure<ClientBrandManualResponse>(ClientBrandManualErrors.NotFound);
        }

        return Result.Success(ClientBrandManualResponse.FromDomain(manual));
    }
}
