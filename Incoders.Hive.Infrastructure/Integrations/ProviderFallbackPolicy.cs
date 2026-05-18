using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Infrastructure.Integrations;

public sealed class ProviderFallbackPolicy : IProviderFallbackPolicy
{
    public ProviderFallbackDecision Decide(
        IntegrationRouteCandidate candidate,
        ProviderFailureDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(diagnostic);

        var isCredentialFailure = IsCredentialFailure(diagnostic.FailureCode);
        var canRetry = diagnostic.IsRetryable
            && !isCredentialFailure
            && diagnostic.AttemptNumber < candidate.MaxAttempts;
        var canFallback = diagnostic.FallbackAllowed
            || candidate.AllowManagedFallback
            || candidate.ConnectionMode != IntegrationConnectionMode.ManagedByIncoders;

        if (canRetry)
        {
            return new ProviderFallbackDecision(
                ShouldRetry: true,
                ShouldFallback: false,
                ReasonCode: "retryable_failure");
        }

        return new ProviderFallbackDecision(
            ShouldRetry: false,
            ShouldFallback: canFallback,
            ReasonCode: isCredentialFailure ? "credential_failure" : "retry_exhausted");
    }

    private static bool IsCredentialFailure(string failureCode) =>
        failureCode.Contains("credential", StringComparison.OrdinalIgnoreCase)
        || failureCode.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
        || failureCode.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
        || failureCode.Contains("401", StringComparison.OrdinalIgnoreCase)
        || failureCode.Contains("403", StringComparison.OrdinalIgnoreCase);
}
