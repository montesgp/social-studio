using Incoders.Hive.Domain.Common;
using MediatR;
using Incoders.Hive.Application.Features.Integrations;

namespace Incoders.Hive.Application.Features.Integrations.GetOverview;

public sealed record GetOrganizationIntegrationsQuery(Guid OrganizationId)
    : IRequest<Result<OrganizationIntegrationsOverviewResponse>>;
