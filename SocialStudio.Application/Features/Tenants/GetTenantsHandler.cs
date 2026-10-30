using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Application.Common.Pagination;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Tenants;

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
