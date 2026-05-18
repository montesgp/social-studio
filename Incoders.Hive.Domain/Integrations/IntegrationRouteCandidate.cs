using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Integrations;

public sealed record IntegrationRouteCandidate(
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
    public const int ProviderKeyMaxLength = 64;
    public const int SelectedModelMaxLength = 200;

    public bool IsManagedFallback => ConnectionMode == IntegrationConnectionMode.ManagedByIncoders;

    public static Result<IntegrationRouteCandidate> Create(
        Guid providerId,
        string providerKey,
        IntegrationConnectionMode connectionMode,
        int priority,
        bool allowManagedFallback,
        int maxAttempts,
        int timeoutMs,
        string? selectedModel,
        bool isEnabled)
    {
        if (providerId == Guid.Empty)
        {
            return Result.Failure<IntegrationRouteCandidate>(IntegrationErrors.RoutingCandidateProviderRequired);
        }

        var normalizedProviderKey = (providerKey ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedProviderKey) || normalizedProviderKey.Length > ProviderKeyMaxLength)
        {
            return Result.Failure<IntegrationRouteCandidate>(IntegrationErrors.RoutingCandidateProviderRequired);
        }

        if (priority <= 0)
        {
            return Result.Failure<IntegrationRouteCandidate>(IntegrationErrors.RoutingCandidatePriorityInvalid);
        }

        if (maxAttempts <= 0)
        {
            return Result.Failure<IntegrationRouteCandidate>(IntegrationErrors.RoutingCandidateAttemptsInvalid);
        }

        if (timeoutMs <= 0)
        {
            return Result.Failure<IntegrationRouteCandidate>(IntegrationErrors.RoutingCandidateTimeoutInvalid);
        }

        if (connectionMode == IntegrationConnectionMode.ManagedByIncoders && !allowManagedFallback)
        {
            return Result.Failure<IntegrationRouteCandidate>(IntegrationErrors.RoutingCandidateManagedFallbackNotAllowed);
        }

        var normalizedModel = string.IsNullOrWhiteSpace(selectedModel) ? null : selectedModel.Trim();
        if (normalizedModel?.Length > SelectedModelMaxLength)
        {
            return Result.Failure<IntegrationRouteCandidate>(IntegrationErrors.RoutingCandidateProviderRequired);
        }

        return Result.Success(new IntegrationRouteCandidate(
            providerId,
            normalizedProviderKey,
            connectionMode,
            priority,
            allowManagedFallback,
            maxAttempts,
            timeoutMs,
            normalizedModel,
            isEnabled));
    }
}