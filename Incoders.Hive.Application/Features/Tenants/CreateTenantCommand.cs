using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Tenants;

public sealed record CreateTenantCommand(string Name) : IRequest<Result<TenantResponse>>;
