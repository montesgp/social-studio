using SocialStudio.Domain.Common;
using MediatR;
using SocialStudio.Application.Features.Integrations;

namespace SocialStudio.Application.Features.Integrations.GetByProvider;

public sealed record GetOrganizationIntegrationByProviderQuery(Guid OrganizationId, string ProviderKey)
    : IRequest<Result<OrganizationIntegrationResponse>>;
