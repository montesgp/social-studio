using Incoders.Hive.Domain.Common;
using MediatR;
using Incoders.Hive.Application.Features.Integrations;

namespace Incoders.Hive.Application.Features.Integrations.GetByProvider;

public sealed record GetOrganizationIntegrationByProviderQuery(Guid OrganizationId, string ProviderKey)
    : IRequest<Result<OrganizationIntegrationResponse>>;
