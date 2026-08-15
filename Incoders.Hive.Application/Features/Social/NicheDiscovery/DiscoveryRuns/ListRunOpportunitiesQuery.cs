using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.Scoring;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.NicheDiscovery;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>
/// Ranked opportunity list for one run (design §10). A run that has not reached a terminal status yet
/// returns <c>run_already_running</c> rather than an empty list — an empty list would be
/// indistinguishable from "ran and genuinely found nothing" (ND-02).
/// </summary>
public sealed record ListRunOpportunitiesQuery(Guid OrganizationId, Guid RunId) : IRequest<Result<RunOpportunitiesResponse>>;

public sealed class ListRunOpportunitiesHandler : IRequestHandler<ListRunOpportunitiesQuery, Result<RunOpportunitiesResponse>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDiscoveryRunRepository _runRepository;
    private readonly INicheOpportunityRepository _opportunityRepository;

    public ListRunOpportunitiesHandler(IDiscoveryRunRepository runRepository, INicheOpportunityRepository opportunityRepository)
    {
        _runRepository = runRepository;
        _opportunityRepository = opportunityRepository;
    }

    public async Task<Result<RunOpportunitiesResponse>> Handle(ListRunOpportunitiesQuery request, CancellationToken cancellationToken)
    {
        var run = await _runRepository.GetByIdAsync(request.OrganizationId, new DiscoveryRunId(request.RunId), cancellationToken);
        if (run is null)
        {
            return Result.Failure<RunOpportunitiesResponse>(NicheDiscoveryErrors.RunNotFound);
        }

        if (run.Status is DiscoveryRunStatus.Requested or DiscoveryRunStatus.Running)
        {
            return Result.Failure<RunOpportunitiesResponse>(NicheDiscoveryErrors.RunAlreadyRunning);
        }

        var records = await _opportunityRepository.ListByRunAsync(request.OrganizationId, request.RunId, cancellationToken);

        var summaries = records
            .Select(r => NicheOpportunitySummary.FromRecord(r, DeserializeExcludedSignals(r.ExcludedSignalsJson)))
            .OrderBy(s => s.Rank)
            .ToList();

        return Result.Success(new RunOpportunitiesResponse(summaries));
    }

    private static IReadOnlyList<ExcludedSignal> DeserializeExcludedSignals(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ExcludedSignal>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
