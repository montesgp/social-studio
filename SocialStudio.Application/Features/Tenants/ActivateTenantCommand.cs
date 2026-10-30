using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Tenants;

public sealed record ActivateTenantCommand(Guid Id) : IRequest<Result<TenantResponse>>;
