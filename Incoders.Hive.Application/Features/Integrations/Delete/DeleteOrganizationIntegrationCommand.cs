using Incoders.Hive.Domain.Common;
using MediatR;
using Incoders.Hive.Application.Features.Integrations;

namespace Incoders.Hive.Application.Features.Integrations.Delete;

public sealed record DeleteOrganizationIntegrationCommand(Guid OrganizationId, string ProviderKey)
    : IRequest<Result>;
