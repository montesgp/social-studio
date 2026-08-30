using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.Scoring;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.NicheDiscovery;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>Opportunity detail plus its navigable channel/video evidence (design §10).</summary>
public sealed record GetOpportunityDetailQuery(Guid OrganizationId, Guid OpportunityId) : IRequest<Result<OpportunityDetailResponse>>;

public sealed class GetOpportunityDetailHandler : IRequestHandler<GetOpportunityDetailQuery, Result<OpportunityDetailResponse>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly INicheOpportunityRepository _opportunityRepository;
    private readonly INicheDiscoveryEvidenceRepository _evidenceRepository;

    public GetOpportunityDetailHandler(
        INicheOpportunityRepository opportunityRepository, INicheDiscoveryEvidenceRepository evidenceRepository)
    {
        _opportunityRepository = opportunityRepository;
        _evidenceRepository = evidenceRepository;
    }

    public async Task<Result<OpportunityDetailResponse>> Handle(GetOpportunityDetailQuery request, CancellationToken cancellationToken)
    {
        var opportunity = await _opportunityRepository.GetByIdAsync(request.OrganizationId, request.OpportunityId, cancellationToken);
        if (opportunity is null)
        {
            return Result.Failure<OpportunityDetailResponse>(NicheDiscoveryErrors.OpportunityNotFound);
        }

        var evidence = await _evidenceRepository.ListByOpportunityAsync(request.OrganizationId, request.OpportunityId, cancellationToken);
        var excludedSignals = DeserializeExcludedSignals(opportunity.ExcludedSignalsJson);

        return Result.Success(new OpportunityDetailResponse(opportunity, excludedSignals, evidence));
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
