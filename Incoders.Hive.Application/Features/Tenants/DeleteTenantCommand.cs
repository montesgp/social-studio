using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Tenants;

public sealed record DeleteTenantCommand(Guid Id) : IRequest<Result<DeletedTenantResponse>>;

public sealed record DeletedTenantResponse(
    Guid Id,
    string Name,
    int DeletedClientCount,
    int DeletedUserCount);
