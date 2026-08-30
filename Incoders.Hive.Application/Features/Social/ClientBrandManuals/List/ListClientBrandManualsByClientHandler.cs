using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientBrandManuals.List;

public sealed class ListClientBrandManualsByClientHandler
    : IRequestHandler<ListClientBrandManualsByClientQuery, Result<PagedClientBrandManualsResponse>>
{
    private readonly IClientBrandManualRepository _manuals;
    private readonly ISocialClientRepository _clients;

    public ListClientBrandManualsByClientHandler(
        IClientBrandManualRepository manuals,
        ISocialClientRepository clients)
    {
        _manuals = manuals;
        _clients = clients;
    }

    public async Task<Result<PagedClientBrandManualsResponse>> Handle(
        ListClientBrandManualsByClientQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<PagedClientBrandManualsResponse>(ClientBrandManualErrors.NotFound);
        }

        var (items, totalCount) = await _manuals.ListByClientAsync(
            clientId,
            request.OrganizationId,
            request.IncludeInactive,
            request.Page,
            request.PageSize,
            cancellationToken);

        var responseItems = items
            .Select(ClientBrandManualResponse.FromDomain)
            .ToList();

        return Result.Success(new PagedClientBrandManualsResponse(responseItems, totalCount));
    }
}
