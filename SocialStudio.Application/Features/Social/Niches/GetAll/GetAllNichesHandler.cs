using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Common.Pagination;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Niches.GetAll;

public sealed class GetAllNichesHandler
    : IRequestHandler<GetAllNichesQuery, Result<PageResult<NicheResponse>>>
{
    private readonly INicheRepository _niches;

    public GetAllNichesHandler(INicheRepository niches)
    {
        _niches = niches;
    }

    public async Task<Result<PageResult<NicheResponse>>> Handle(
        GetAllNichesQuery request,
        CancellationToken cancellationToken)
    {
        // The cap lives here rather than at the endpoint so every caller — HTTP, seeder, another
        // handler — is bounded by the same rule.
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize switch
        {
            < 1 => GetAllNichesQuery.DefaultPageSize,
            > GetAllNichesQuery.MaxPageSize => GetAllNichesQuery.MaxPageSize,
            _ => request.PageSize,
        };

        var paged = await _niches.ListAsync(
            new NicheListQuery(request.IncludeInactive, page, pageSize, request.SearchTerm),
            cancellationToken);

        var mapped = new PageResult<NicheResponse>(
            paged.Items.Select(NicheResponse.FromDomain).ToList(),
            paged.Page,
            paged.PageSize,
            paged.TotalCount);

        return Result.Success(mapped);
    }
}
