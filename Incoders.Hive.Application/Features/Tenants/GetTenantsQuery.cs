using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Tenants;

public sealed record GetTenantsQuery(
    string? Name,
    bool IncludeInactive,
    int Page,
    int PageSize) : IRequest<Result<PageResult<TenantResponse>>>;
