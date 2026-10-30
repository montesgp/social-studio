using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Integrations;

public sealed record ProviderFailureDiagnostic(
    string CapabilityKey,
    string ProviderKey,
    int AttemptNumber,
    string FailureCode,
    string MessageKey,
    bool IsRetryable,
    bool FallbackAllowed,
    int ElapsedMs,
    DateTime OccurredAtUtc)
{
    public static Result<ProviderFailureDiagnostic> Create(
        string capabilityKey,
        string providerKey,
        int attemptNumber,
        string failureCode,
        string messageKey,
        bool isRetryable,
        bool fallbackAllowed,
        int elapsedMs,
        DateTime occurredAtUtc)
    {
        var normalizedCapability = NormalizeKey(capabilityKey);
        var normalizedProvider = NormalizeKey(providerKey);
        var normalizedFailure = NormalizeKey(failureCode);
        var normalizedMessageKey = (messageKey ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(normalizedCapability)
            || string.IsNullOrWhiteSpace(normalizedProvider)
            || string.IsNullOrWhiteSpace(normalizedFailure)
            || string.IsNullOrWhiteSpace(normalizedMessageKey)
            || attemptNumber <= 0
            || elapsedMs < 0)
        {
            return Result.Failure<ProviderFailureDiagnostic>(IntegrationErrors.ProviderFailureDiagnosticInvalid);
        }

        return Result.Success(new ProviderFailureDiagnostic(
            normalizedCapability,
            normalizedProvider,
            attemptNumber,
            normalizedFailure,
            normalizedMessageKey,
            isRetryable,
            fallbackAllowed,
            elapsedMs,
            occurredAtUtc));
    }

    private static string NormalizeKey(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}