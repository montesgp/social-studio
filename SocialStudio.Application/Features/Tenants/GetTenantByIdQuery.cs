using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Tenants;

public sealed record GetTenantByIdQuery(Guid Id) : IRequest<Result<TenantResponse>>;
