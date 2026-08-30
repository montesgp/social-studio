using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.GetById;

/// <param name="OrganizationId">
/// The organization the lookup is scoped to. <c>null</c> means "resolve across every organization"
/// and is reserved for platform admins — it mirrors <see cref="GetAll.GetSocialClientsQuery"/>,
/// whose null organization already means "all organizations". The endpoint is responsible for only
/// passing null when the caller is a platform admin.
/// </param>
public sealed record GetSocialClientByIdQuery(
    Guid Id,
    Guid? OrganizationId) : IRequest<Result<SocialClientResponse>>;
