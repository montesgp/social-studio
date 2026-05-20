using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Integrations.RoutingPolicies;

public sealed record UpsertOrganizationRoutingPolicyCommand(
    Guid OrganizationId,
    string FlowKey,
    string CapabilityKey,
    string? PlatformKey,
    bool IsEnabled,
    IReadOnlyList<UpsertOrganizationRoutingPolicyCandidateInput> Candidates)
    : IRequest<Result<OrganizationRoutingPolicyResponse>>;

public sealed record UpsertOrganizationRoutingPolicyCandidateInput(
    string ProviderKey,
    int Priority,
    bool AllowManagedFallback,
    int MaxAttempts,
    int TimeoutMs,
    string? SelectedModel,
    bool IsEnabled);
