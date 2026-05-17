using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Tenants;

public sealed class GetTenantsHandler : IRequestHandler<GetTenantsQuery, Result<PageResult<TenantResponse>>>
{
    private readonly ITenantRepository _tenants;

    public GetTenantsHandler(ITenantRepository tenants)
    {
        _tenants = tenants;
    }

    public async Task<Result<PageResult<TenantResponse>>> Handle(GetTenantsQuery request, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _tenants.ListAsync(
            new TenantQuery(request.Name, request.IncludeInactive, request.Page, request.PageSize),
            cancellationToken);

        var response = new PageResult<TenantResponse>(
            items.Select(TenantResponse.FromDomain).ToList(),
            request.Page,
            request.PageSize,
            totalCount);

        return Result.Success(response);
    }
}
