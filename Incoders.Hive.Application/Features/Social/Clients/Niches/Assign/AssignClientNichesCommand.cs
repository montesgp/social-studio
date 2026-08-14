using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.Niches.Assign;

/// <summary>
/// Replaces the client's whole niche selection. A <c>null</c> <paramref name="OrganizationId"/> is
/// the platform-admin path, mirroring <c>GetSocialClientByIdQuery</c>.
/// </summary>
public sealed record AssignClientNichesCommand(
    Guid ClientId,
    Guid? OrganizationId,
    IReadOnlyList<Guid> NicheIds) : IRequest<Result<ClientNichesResponse>>;
