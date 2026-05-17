using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Tenants;

public sealed record DeactivateTenantCommand(Guid Id) : IRequest<Result<TenantResponse>>;
