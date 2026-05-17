using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Tenants;
using MediatR;

namespace Incoders.Hive.Application.Features.Tenants;

public sealed class GetTenantByIdHandler : IRequestHandler<GetTenantByIdQuery, Result<TenantResponse>>
{
    private readonly ITenantRepository _tenants;

    public GetTenantByIdHandler(ITenantRepository tenants)
    {
        _tenants = tenants;
    }

    public async Task<Result<TenantResponse>> Handle(GetTenantByIdQuery request, CancellationToken cancellationToken)
    {
        var tenant = await _tenants.GetByIdAsync(new TenantId(request.Id), cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<TenantResponse>(TenantErrors.NotFound);
        }

        return Result.Success(TenantResponse.FromDomain(tenant));
    }
}
