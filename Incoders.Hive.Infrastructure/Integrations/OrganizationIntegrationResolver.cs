using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Infrastructure.Integrations;

public sealed class OrganizationIntegrationResolver : IOrganizationIntegrationResolver
{
    private readonly IIntegrationRoutingPolicyRepository _routingPolicies;

    public OrganizationIntegrationResolver(IIntegrationRoutingPolicyRepository routingPolicies)
    {
        _routingPolicies = routingPolicies;
    }

    public async Task<Result<IntegrationRouteResolution>> ResolveRouteAsync(
        IntegrationRouteRequest request,
        CancellationToken cancellationToken = default)
    {
        var policy = await _routingPolicies.GetEnabledPolicyAsync(
            request.OrganizationId,
            request.FlowKey,
            request.CapabilityKey,
            request.PlatformKey,
            cancellationToken);

        if (policy is null)
        {
            return Result.Failure<IntegrationRouteResolution>(IntegrationErrors.RoutingPolicyNotConfigured);
        }

        var candidates = policy.GetEnabledCandidates();
        if (!string.IsNullOrWhiteSpace(request.PreferredProviderKey))
        {
            var preferred = request.PreferredProviderKey.Trim().ToLowerInvariant();
            candidates = candidates
                .OrderBy(candidate => candidate.ProviderKey == preferred ? 0 : 1)
                .ThenBy(candidate => candidate.Priority)
                .Select(candidate => candidate.ProviderKey == preferred && !string.IsNullOrWhiteSpace(request.PreferredModel)
                    ? candidate with { SelectedModel = request.PreferredModel.Trim() }
                    : candidate)
                .ToArray();
        }

        return candidates.Count == 0
            ? Result.Failure<IntegrationRouteResolution>(IntegrationErrors.RoutingPolicyNotConfigured)
            : Result.Success(new IntegrationRouteResolution(candidates, []));
    }
}
