using SocialStudio.Domain.Common;
using MediatR;
using SocialStudio.Application.Features.Integrations;

namespace SocialStudio.Application.Features.Integrations.TestConnection;

public sealed record TestOrganizationIntegrationConnectionCommand(Guid OrganizationId, string ProviderKey)
    : IRequest<Result<OrganizationIntegrationResponse>>;
