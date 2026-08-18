using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.NicheDiscovery;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>
/// The organization's discovery-run history, newest first (design §10).
///
/// A run's result is expensive — provider quota, AI completions, and an operator's review time — and it
/// was already persisted per organization. Without this the history existed but was unreachable: closing
/// the tool lost the way back to it. Scoped from the authenticated tenant, never from the request.
///
/// Deliberately a SUMMARY: the opportunity list and its evidence run to hundreds of rows, so the history
/// carries only what it takes to decide which run to reopen.
/// </summary>
public sealed record ListDiscoveryRunsQuery(Guid OrganizationId, int Limit) : IRequest<Result<DiscoveryRunHistoryResponse>>;

/// <summary>One row of the history — enough to choose a run, not enough to analyse one.</summary>
public sealed record DiscoveryRunHistoryEntry(
    Guid RunId,
    string Status,
    string SourceMarketCode,
    string TargetMarketCode,
    string ContentFormat,
    int WindowDays,
    string Depth,
    IReadOnlyList<string> SeedTerms,
    bool SemanticDegraded,
    string? FailureReasonKey,
    int ProviderUnitsSpent,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record DiscoveryRunHistoryResponse(IReadOnlyList<DiscoveryRunHistoryEntry> Runs);

public sealed class ListDiscoveryRunsHandler : IRequestHandler<ListDiscoveryRunsQuery, Result<DiscoveryRunHistoryResponse>>
{
    /// <summary>Keeps a long-lived organization's history from becoming an unbounded query.</summary>
    public const int MaxLimit = 100;

    public const int DefaultLimit = 25;

    private readonly IDiscoveryRunRepository _runRepository;

    public ListDiscoveryRunsHandler(IDiscoveryRunRepository runRepository)
    {
        _runRepository = runRepository;
    }

    public async Task<Result<DiscoveryRunHistoryResponse>> Handle(
        ListDiscoveryRunsQuery request, CancellationToken cancellationToken)
    {
        var limit = request.Limit <= 0 ? DefaultLimit : Math.Min(request.Limit, MaxLimit);

        var runs = await _runRepository.ListByOrganizationAsync(request.OrganizationId, limit, cancellationToken);

        var entries = runs.Select(ToEntry).ToList();

        return Result.Success(new DiscoveryRunHistoryResponse(entries));
    }

    private static DiscoveryRunHistoryEntry ToEntry(DiscoveryRun run)
    {
        // The seed terms say WHAT was asked, and their absence says the run was an open discovery — the
        // one thing that distinguishes two otherwise identical-looking rows in the history.
        var snapshot = DiscoveryRunConfigurationSnapshot.FromJson(run.ConfigurationSnapshotJson);

        return new DiscoveryRunHistoryEntry(
            RunId: run.Id.Value,
            Status: run.Status.ToString(),
            SourceMarketCode: run.SourceMarketCode.Value,
            TargetMarketCode: run.TargetMarketCode.Value,
            ContentFormat: run.ContentFormat,
            WindowDays: run.Window.Days,
            Depth: run.Depth.ToString(),
            SeedTerms: snapshot.SeedTerms,
            SemanticDegraded: run.SemanticDegraded,
            FailureReasonKey: run.FailureReasonKey,
            ProviderUnitsSpent: run.ProviderUnitsSpent,
            CreatedAtUtc: new DateTimeOffset(run.CreatedAtUtc, TimeSpan.Zero),
            CompletedAtUtc: run.CompletedAtUtc is null ? null : new DateTimeOffset(run.CompletedAtUtc.Value, TimeSpan.Zero));
    }
}
