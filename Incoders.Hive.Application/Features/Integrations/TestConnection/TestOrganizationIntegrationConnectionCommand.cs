using Incoders.Hive.Domain.Common;
using MediatR;
using Incoders.Hive.Application.Features.Integrations;

namespace Incoders.Hive.Application.Features.Integrations.TestConnection;

public sealed record TestOrganizationIntegrationConnectionCommand(Guid OrganizationId, string ProviderKey)
    : IRequest<Result<OrganizationIntegrationResponse>>;
