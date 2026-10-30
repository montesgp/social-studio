using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Tenants;

public sealed record DeactivateTenantCommand(Guid Id) : IRequest<Result<TenantResponse>>;
