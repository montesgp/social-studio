using SocialStudio.Application.Common.Pagination;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Niches.GetAll;

/// <summary>
/// Bounded catalog query. Unlike the social network catalog — which is three rows and will stay
/// three rows — the niche catalog grows, so the caller never gets to ask for all of it. A caller
/// with no search term gets the most recently created entries, capped at <see cref="MaxPageSize"/>.
/// </summary>
public sealed record GetAllNichesQuery(
    bool IncludeInactive,
    int Page,
    int PageSize,
    string? SearchTerm) : IRequest<Result<PageResult<NicheResponse>>>
{
    public const int MaxPageSize = 50;

    public const int DefaultPageSize = 20;
}
