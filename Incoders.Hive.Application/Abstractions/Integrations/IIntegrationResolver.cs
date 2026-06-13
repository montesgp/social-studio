using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Abstractions.Integrations;

public interface IIntegrationResolver
{
    Task<Result<ResolvedIntegration>> ResolveProviderAsync(
        Guid organizationId,
        string providerKey,
        CancellationToken cancellationToken = default);
}

public sealed record ResolvedIntegration(
    Guid OrganizationId,
    string ProviderKey,
    string ProviderDisplayName,
    IntegrationProviderCategory Category,
    IntegrationConnectionMode ConnectionMode,
    IntegrationStatus Status,
    bool IsReadOnly,
    string? SelectedAiModel,
    IReadOnlyDictionary<string, string> Credentials);