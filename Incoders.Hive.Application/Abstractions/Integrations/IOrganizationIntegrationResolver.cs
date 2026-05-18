using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Abstractions.Integrations;

public interface IOrganizationIntegrationResolver
{
    Task<Result<IntegrationRouteResolution>> ResolveRouteAsync(
        IntegrationRouteRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record IntegrationRouteRequest(
    Guid OrganizationId,
    string FlowKey,
    string CapabilityKey,
    string? PlatformKey,
    string? PreferredProviderKey,
    string? PreferredModel);

public sealed record IntegrationRouteResolution(
    IReadOnlyList<IntegrationRouteCandidate> Candidates,
    IReadOnlyList<ProviderFailureDiagnostic> Diagnostics);