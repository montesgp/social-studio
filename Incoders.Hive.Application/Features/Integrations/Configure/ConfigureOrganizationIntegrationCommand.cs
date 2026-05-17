using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using MediatR;
using Incoders.Hive.Application.Features.Integrations;

namespace Incoders.Hive.Application.Features.Integrations.Configure;

public sealed record ConfigureOrganizationIntegrationCommand(
    Guid OrganizationId,
    string ProviderKey,
    IntegrationConnectionMode ConnectionMode,
    bool Enabled,
    string? SelectedAiModel,
    IReadOnlyDictionary<string, string> Credentials)
    : IRequest<Result<OrganizationIntegrationResponse>>;
