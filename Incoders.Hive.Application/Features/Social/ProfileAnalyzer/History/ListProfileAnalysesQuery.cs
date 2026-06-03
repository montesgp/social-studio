using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ProfileAnalyzer.History;

/// <summary>Paged list of profile analyses for an organization (PA-15).</summary>
public sealed record ListProfileAnalysesQuery(
    Guid OrganizationId,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<ProfileAnalysisHistoryResponse>>;

public sealed class ListProfileAnalysesQueryHandler
    : IRequestHandler<ListProfileAnalysesQuery, Result<ProfileAnalysisHistoryResponse>>
{
    private readonly IProfileAnalysisRepository _repository;

    public ListProfileAnalysesQueryHandler(IProfileAnalysisRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<ProfileAnalysisHistoryResponse>> Handle(
        ListProfileAnalysesQuery request,
        CancellationToken cancellationToken)
    {
        var page = await _repository.GetByOrganizationAsync(
            request.OrganizationId,
            request.Page,
            request.PageSize,
            cancellationToken);

        var items = page.Items
            .Select(item => new ProfileAnalysisHistoryItemDto(
                item.Id,
                item.ClientId,
                item.ProfileUrlOrHandle,
                item.Status,
                item.FailureCode,
                item.CreatedAtUtc,
                item.CompletedAtUtc))
            .ToList();

        return Result.Success(new ProfileAnalysisHistoryResponse(
            items,
            page.TotalCount,
            page.Page,
            page.PageSize));
    }
}
