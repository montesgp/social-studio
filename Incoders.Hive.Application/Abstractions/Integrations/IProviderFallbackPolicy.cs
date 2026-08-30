using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Abstractions.Integrations;

public interface IProviderFallbackPolicy
{
    ProviderFallbackDecision Decide(
        IntegrationRouteCandidate candidate,
        ProviderFailureDiagnostic diagnostic);
}

public sealed record ProviderFallbackDecision(
    bool ShouldRetry,
    bool ShouldFallback,
    string ReasonCode);