using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using MediatR;

namespace Incoders.Hive.Application.Features.Integrations.RoutingPolicies;

public sealed class ListOrganizationRoutingPoliciesHandler
    : IRequestHandler<ListOrganizationRoutingPoliciesQuery, Result<IReadOnlyList<OrganizationRoutingPolicyResponse>>>
{
    private readonly IIntegrationRoutingPolicyRepository _routingPolicies;

    public ListOrganizationRoutingPoliciesHandler(IIntegrationRoutingPolicyRepository routingPolicies)
    {
        _routingPolicies = routingPolicies;
    }

    public async Task<Result<IReadOnlyList<OrganizationRoutingPolicyResponse>>> Handle(
        ListOrganizationRoutingPoliciesQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<IReadOnlyList<OrganizationRoutingPolicyResponse>>(IntegrationErrors.OrganizationRequired);
        }

        var policies = await _routingPolicies.ListByOrganizationAsync(
            request.OrganizationId,
            request.FlowKey,
            request.CapabilityKey,
            request.PlatformKey,
            cancellationToken);

        return Result.Success<IReadOnlyList<OrganizationRoutingPolicyResponse>>(
            policies.Select(OrganizationRoutingPolicyResponse.FromDomain).ToArray());
    }
}
