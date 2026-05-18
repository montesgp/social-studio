using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Networks.GetAll;

public sealed record GetAllSocialNetworksQuery(
    bool IncludeInactive,
    int Page,
    int PageSize,
    string? SearchTerm) : IRequest<Result<PageResult<SocialNetworkResponse>>>;
