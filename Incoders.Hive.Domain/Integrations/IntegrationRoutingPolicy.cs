using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Integrations;

public sealed class IntegrationRoutingPolicy
{
    public const int FlowKeyMaxLength = 64;
    public const int CapabilityKeyMaxLength = 64;
    public const int PlatformKeyMaxLength = 64;

    private IntegrationRoutingPolicy(
        Guid id,
        Guid organizationId,
        string flowKey,
        string capabilityKey,
        string? platformKey,
        bool isEnabled,
        IReadOnlyList<IntegrationRouteCandidate> candidates,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        FlowKey = flowKey;
        CapabilityKey = capabilityKey;
        PlatformKey = platformKey;
        IsEnabled = isEnabled;
        Candidates = candidates;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public string FlowKey { get; }

    public string CapabilityKey { get; }

    public string? PlatformKey { get; }

    public bool IsEnabled { get; }

    public IReadOnlyList<IntegrationRouteCandidate> Candidates { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; }

    public static Result<IntegrationRoutingPolicy> Create(
        Guid id,
        Guid organizationId,
        string flowKey,
        string capabilityKey,
        string? platformKey,
        bool isEnabled,
        IReadOnlyList<IntegrationRouteCandidate> candidates,
        DateTime nowUtc)
    {
        if (organizationId == Guid.Empty)
        {
            return Result.Failure<IntegrationRoutingPolicy>(IntegrationErrors.RoutingPolicyOrganizationRequired);
        }

        var normalizedFlow = NormalizeKey(flowKey);
        if (string.IsNullOrWhiteSpace(normalizedFlow) || normalizedFlow.Length > FlowKeyMaxLength)
        {
            return Result.Failure<IntegrationRoutingPolicy>(IntegrationErrors.RoutingPolicyFlowRequired);
        }

        var normalizedCapability = NormalizeKey(capabilityKey);
        if (string.IsNullOrWhiteSpace(normalizedCapability) || normalizedCapability.Length > CapabilityKeyMaxLength)
        {
            return Result.Failure<IntegrationRoutingPolicy>(IntegrationErrors.RoutingPolicyCapabilityRequired);
        }

        var normalizedPlatform = NormalizeOptionalKey(platformKey);
        if (normalizedPlatform?.Length > PlatformKeyMaxLength)
        {
            return Result.Failure<IntegrationRoutingPolicy>(IntegrationErrors.RoutingPolicyCapabilityRequired);
        }

        var normalizedCandidates = (candidates ?? [])
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.ProviderKey, StringComparer.Ordinal)
            .ToList();

        var enabledCandidates = normalizedCandidates.Where(x => x.IsEnabled).ToList();
        if (isEnabled && enabledCandidates.Count == 0)
        {
            return Result.Failure<IntegrationRoutingPolicy>(IntegrationErrors.RoutingPolicyRequiresEnabledCandidate);
        }

        var duplicateEnabledPriority = enabledCandidates
            .GroupBy(x => x.Priority)
            .Any(group => group.Count() > 1);

        if (duplicateEnabledPriority)
        {
            return Result.Failure<IntegrationRoutingPolicy>(IntegrationErrors.RoutingPolicyDuplicatePriority);
        }

        return Result.Success(new IntegrationRoutingPolicy(
            id == Guid.Empty ? Guid.NewGuid() : id,
            organizationId,
            normalizedFlow,
            normalizedCapability,
            normalizedPlatform,
            isEnabled,
            normalizedCandidates,
            nowUtc,
            nowUtc));
    }

    public static IntegrationRoutingPolicy Restore(
        Guid id,
        Guid organizationId,
        string flowKey,
        string capabilityKey,
        string? platformKey,
        bool isEnabled,
        IReadOnlyList<IntegrationRouteCandidate> candidates,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new IntegrationRoutingPolicy(
            id,
            organizationId,
            flowKey,
            capabilityKey,
            platformKey,
            isEnabled,
            candidates.OrderBy(x => x.Priority).ThenBy(x => x.ProviderKey, StringComparer.Ordinal).ToList(),
            createdAtUtc,
            updatedAtUtc);
    }

    public IReadOnlyList<IntegrationRouteCandidate> GetEnabledCandidates()
    {
        return Candidates
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.ProviderKey, StringComparer.Ordinal)
            .ToList();
    }

    private static string NormalizeKey(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string? NormalizeOptionalKey(string? value)
    {
        var normalized = NormalizeKey(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}