using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Tenants;

public sealed record CreateTenantCommand(string Name) : IRequest<Result<TenantResponse>>;
