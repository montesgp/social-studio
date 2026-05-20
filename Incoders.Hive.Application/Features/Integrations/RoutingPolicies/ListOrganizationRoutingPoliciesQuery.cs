using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Integrations.RoutingPolicies;

public sealed record ListOrganizationRoutingPoliciesQuery(
    Guid OrganizationId,
    string? FlowKey,
    string? CapabilityKey,
    string? PlatformKey)
    : IRequest<Result<IReadOnlyList<OrganizationRoutingPolicyResponse>>>;
