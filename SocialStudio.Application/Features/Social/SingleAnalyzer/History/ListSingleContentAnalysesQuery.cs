using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.SingleAnalyzer;
using MediatR;

namespace SocialStudio.Application.Features.Social.SingleAnalyzer.History;

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
    Guid? PackageJobId,
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
            .Select(SingleContentAnalysisHistoryMapping.ToResponse)
            .ToList();

        return Result.Success(new SingleContentAnalysisHistoryResponse(
            items,
            page.TotalCount,
            page.Page,
            page.PageSize));
    }
}

internal static class SingleContentAnalysisHistoryMapping
{
    internal static SingleContentAnalysisHistoryItemResponse ToResponse(SingleContentAnalysisHistoryItem item) =>
        new(
            item.Id,
            item.ClientId,
            item.SourceUrl,
            item.NormalizedUrl,
            item.SourcePlatformKey,
            item.Status,
            item.FailureCode,
            item.CreatedAtUtc,
            item.UpdatedAtUtc,
            item.CompletedAtUtc,
            item.LatestContentGeneration is null
                ? null
                : new ContentGenerationSummaryResponse(
                    item.LatestContentGeneration.JobId,
                    item.LatestContentGeneration.Status,
                    item.LatestContentGeneration.HasPackage,
                    item.LatestContentGeneration.PackageJobId,
                    item.LatestContentGeneration.CompletedAt));
}
