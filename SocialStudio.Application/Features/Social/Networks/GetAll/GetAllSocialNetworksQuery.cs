using SocialStudio.Application.Common.Pagination;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Networks.GetAll;

public sealed record GetAllSocialNetworksQuery(
    bool IncludeInactive,
    int Page,
    int PageSize,
    string? SearchTerm) : IRequest<Result<PageResult<SocialNetworkResponse>>>;
