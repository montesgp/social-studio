using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.GetAll;

public sealed record GetSocialClientsQuery(
    Guid OrganizationId,
    string? Name,
    bool IncludeInactive,
    int Page,
    int PageSize) : IRequest<Result<PageResult<SocialClientResponse>>>;
