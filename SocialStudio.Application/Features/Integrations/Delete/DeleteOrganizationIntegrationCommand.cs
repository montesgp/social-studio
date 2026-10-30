using SocialStudio.Domain.Common;
using MediatR;
using SocialStudio.Application.Features.Integrations;

namespace SocialStudio.Application.Features.Integrations.Delete;

public sealed record DeleteOrganizationIntegrationCommand(Guid OrganizationId, string ProviderKey)
    : IRequest<Result>;
