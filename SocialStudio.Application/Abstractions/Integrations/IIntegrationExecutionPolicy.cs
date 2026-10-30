using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;

namespace SocialStudio.Application.Abstractions.Integrations;

public interface IIntegrationExecutionPolicy
{
    Task<Result<ProviderExecutionResult<TResponse>>> ExecuteAsync<TResponse>(
        IntegrationExecutionRequest request,
        IReadOnlyList<IntegrationRouteCandidate> candidates,
        Func<IntegrationRouteCandidate, CancellationToken, Task<Result<TResponse>>> executeAsync,
        CancellationToken cancellationToken = default)
        where TResponse : notnull;
}

public sealed record IntegrationExecutionRequest(
    Guid OrganizationId,
    Guid? AnalysisId,
    string FlowKey,
    string CapabilityKey,
    string? PlatformKey,
    Guid? CorrelationId = null,
    string? CorrelationKind = null);

public sealed record ProviderExecutionResult<TResponse>(
    TResponse Payload,
    IntegrationRouteCandidate Candidate,
    IReadOnlyList<ProviderFailureDiagnostic> Diagnostics)
    where TResponse : notnull;