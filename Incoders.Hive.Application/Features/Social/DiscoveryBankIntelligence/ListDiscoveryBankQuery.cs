using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.DiscoveryBankIntelligence;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.DiscoveryBankIntelligence;

/// <summary>
/// The bank as a readable table — the left half of the AI Analysis page.
///
/// It exists so the chat can be CHECKED. An operator who is told "@handle leads on views" can scan the
/// same rows the system ranked and confirm it, which is the difference between a tool that informs a
/// budget decision and one that has to be trusted.
/// </summary>
public sealed record ListDiscoveryBankQuery(
    Guid OrganizationId,
    int Page,
    int PageSize) : IRequest<Result<DiscoveryBankPageResponse>>;

public sealed record DiscoveryBankPageResponse(
    IReadOnlyList<DiscoveryBankCandidateResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<DiscoveryBankSupportedQueryResponse> SupportedQueries);

public sealed record DiscoveryBankCandidateResponse(
    string CandidateKey,
    string NetworkKey,
    string ContentUrl,
    string AuthorHandle,
    string Title,
    IReadOnlyList<string> Hashtags,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset ObservedAtUtc,
    long? Views,
    long? Likes,
    long? Comments,
    long? Shares,
    long? Saves,
    double? OpportunityScore);

public sealed class ListDiscoveryBankHandler
    : IRequestHandler<ListDiscoveryBankQuery, Result<DiscoveryBankPageResponse>>
{
    private const int MaxPageSize = 200;

    private readonly IDiscoveryBankReader _bank;

    public ListDiscoveryBankHandler(IDiscoveryBankReader bank)
    {
        _bank = bank;
    }

    public async Task<Result<DiscoveryBankPageResponse>> Handle(
        ListDiscoveryBankQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<DiscoveryBankPageResponse>(DiscoveryBankErrors.OrganizationRequired);
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        var rows = await _bank.ListAsync(request.OrganizationId, cancellationToken);

        // Newest observation first, ties broken on the candidate key: the same page always shows the same
        // rows in the same order, so "row 3" means something between one refresh and the next.
        var ordered = rows
            .OrderByDescending(row => row.ObservedAtUtc)
            .ThenBy(row => row.CandidateKey, StringComparer.Ordinal)
            .ToList();

        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(Map)
            .ToList();

        return Result.Success(new DiscoveryBankPageResponse(
            items,
            ordered.Count,
            page,
            pageSize,
            AskDiscoveryBankQuestionHandler.SupportedQueries()));
    }

    private static DiscoveryBankCandidateResponse Map(DiscoveryBankRow row) => new(
        row.CandidateKey,
        row.NetworkKey,
        row.ContentUrl,
        row.AuthorHandle,
        row.Title,
        row.Hashtags,
        row.PublishedAtUtc,
        row.ObservedAtUtc,
        row.Views,
        row.Likes,
        row.Comments,
        row.Shares,
        row.Saves,
        row.OpportunityScore);
}
