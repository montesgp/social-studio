using SocialStudio.Application.Common.Pagination;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Clients.GetAll;

public sealed record GetSocialClientsQuery(
    Guid? OrganizationId,
    string? Name,
    bool IncludeInactive,
    int Page,
    int PageSize) : IRequest<Result<PageResult<SocialClientResponse>>>;
