using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Features.Integrations.RoutingPolicies;

public sealed record OrganizationRoutingPolicyResponse(
    Guid Id,
    Guid OrganizationId,
    string FlowKey,
    string CapabilityKey,
    string? PlatformKey,
    bool IsEnabled,
    IReadOnlyList<OrganizationRoutingPolicyCandidateResponse> Candidates,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static OrganizationRoutingPolicyResponse FromDomain(IntegrationRoutingPolicy policy) =>
        new(
            policy.Id,
            policy.OrganizationId,
            policy.FlowKey,
            policy.CapabilityKey,
            policy.PlatformKey,
            policy.IsEnabled,
            policy.Candidates
                .OrderBy(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.ProviderKey, StringComparer.Ordinal)
                .Select(OrganizationRoutingPolicyCandidateResponse.FromDomain)
                .ToArray(),
            policy.CreatedAtUtc,
            policy.UpdatedAtUtc);
}

public sealed record OrganizationRoutingPolicyCandidateResponse(
    Guid ProviderId,
    string ProviderKey,
    IntegrationConnectionMode ConnectionMode,
    int Priority,
    bool AllowManagedFallback,
    int MaxAttempts,
    int TimeoutMs,
    string? SelectedModel,
    bool IsEnabled)
{
    public static OrganizationRoutingPolicyCandidateResponse FromDomain(IntegrationRouteCandidate candidate) =>
        new(
            candidate.ProviderId,
            candidate.ProviderKey,
            candidate.ConnectionMode,
            candidate.Priority,
            candidate.AllowManagedFallback,
            candidate.MaxAttempts,
            candidate.TimeoutMs,
            candidate.SelectedModel,
            candidate.IsEnabled);
}
