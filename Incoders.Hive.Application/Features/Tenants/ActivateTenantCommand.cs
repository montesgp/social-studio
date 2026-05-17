using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Tenants;

public sealed record ActivateTenantCommand(Guid Id) : IRequest<Result<TenantResponse>>;
