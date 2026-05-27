using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.SingleAnalyzer.History;

/// <summary>
/// Paged history of a single user's analyses within an organization.
/// Powers the "saved analyses" grid below the analyzer so the user can review past
/// work or resume from where they left off — no need to spend Apify+Claude tokens again.
/// </summary>
public sealed record ListSingleContentAnalysesQuery(
    Guid OrganizationId,
    Guid UserId,
    int Page,
    int PageSize) : IRequest<Result<SingleContentAnalysisHistoryResponse>>;

public sealed record SingleContentAnalysisHistoryResponse(
    IReadOnlyList<SingleContentAnalysisHistoryItemResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record SingleContentAnalysisHistoryItemResponse(
    Guid Id,
    Guid? ClientId,
    string SourceUrl,
    string? NormalizedUrl,
    string? SourcePlatformKey,
    SingleContentAnalysisStatus Status,
    string? FailureCode,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    ContentGenerationSummaryResponse? LatestContentGeneration);

public sealed record ContentGenerationSummaryResponse(
    Guid JobId,
    string Status,
    bool HasPackage,
    DateTime? CompletedAt);

public sealed class ListSingleContentAnalysesHandler
    : IRequestHandler<ListSingleContentAnalysesQuery, Result<SingleContentAnalysisHistoryResponse>>
{
    private readonly ISingleContentAnalysisRepository _analyses;

    public ListSingleContentAnalysesHandler(ISingleContentAnalysisRepository analyses)
    {
        _analyses = analyses;
    }

    public async Task<Result<SingleContentAnalysisHistoryResponse>> Handle(
        ListSingleContentAnalysesQuery request,
        CancellationToken cancellationToken)
    {
        var page = await _analyses.ListByUserAsync(
            request.OrganizationId,
            request.UserId,
            request.Page,
            request.PageSize,
            cancellationToken);

        var items = page.Items
            .Select(i => new SingleContentAnalysisHistoryItemResponse(
                i.Id,
                i.ClientId,
                i.SourceUrl,
                i.NormalizedUrl,
                i.SourcePlatformKey,
                i.Status,
                i.FailureCode,
                i.CreatedAtUtc,
                i.UpdatedAtUtc,
                i.CompletedAtUtc,
                i.LatestContentGeneration is null
                    ? null
                    : new ContentGenerationSummaryResponse(
                        i.LatestContentGeneration.JobId,
                        i.LatestContentGeneration.Status,
                        i.LatestContentGeneration.HasPackage,
                        i.LatestContentGeneration.CompletedAt)))
            .ToList();

        return Result.Success(new SingleContentAnalysisHistoryResponse(
            items,
            page.TotalCount,
            page.Page,
            page.PageSize));
    }
}
