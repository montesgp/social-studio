using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.Delete;

public sealed record DeleteSocialClientCommand(Guid Id) : IRequest<Result<DeletedSocialClientResponse>>;

public sealed record DeletedSocialClientResponse(
    Guid Id,
    Guid OrganizationId,
    string Name);
