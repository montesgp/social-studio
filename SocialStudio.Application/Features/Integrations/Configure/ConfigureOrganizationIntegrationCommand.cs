using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using MediatR;
using SocialStudio.Application.Features.Integrations;

namespace SocialStudio.Application.Features.Integrations.Configure;

public sealed record ConfigureOrganizationIntegrationCommand(
    Guid OrganizationId,
    string ProviderKey,
    IntegrationConnectionMode ConnectionMode,
    bool Enabled,
    string? SelectedAiModel,
    IReadOnlyDictionary<string, string> Credentials)
    : IRequest<Result<OrganizationIntegrationResponse>>;
