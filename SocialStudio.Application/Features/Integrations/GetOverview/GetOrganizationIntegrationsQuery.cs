using SocialStudio.Domain.Common;
using MediatR;
using SocialStudio.Application.Features.Integrations;

namespace SocialStudio.Application.Features.Integrations.GetOverview;

public sealed record GetOrganizationIntegrationsQuery(Guid OrganizationId)
    : IRequest<Result<OrganizationIntegrationsOverviewResponse>>;
