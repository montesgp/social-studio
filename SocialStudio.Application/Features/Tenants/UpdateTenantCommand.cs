using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Tenants;

public sealed record UpdateTenantCommand(Guid Id, string Name) : IRequest<Result<TenantResponse>>;
