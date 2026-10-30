using SocialStudio.Application.Common.Pagination;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Tenants;

public sealed record GetTenantsQuery(
    string? Name,
    bool IncludeInactive,
    int Page,
    int PageSize) : IRequest<Result<PageResult<TenantResponse>>>;
