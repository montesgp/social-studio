using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Tenants;

public sealed record UpdateTenantCommand(Guid Id, string Name) : IRequest<Result<TenantResponse>>;
