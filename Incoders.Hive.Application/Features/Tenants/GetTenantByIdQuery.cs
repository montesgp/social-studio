using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Tenants;

public sealed record GetTenantByIdQuery(Guid Id) : IRequest<Result<TenantResponse>>;
