using Incoders.Hive.Domain.Common;
using MediatR;
using Incoders.Hive.Application.Features.Integrations;

namespace Incoders.Hive.Application.Features.Integrations.SetDefaultAi;

public sealed record SetDefaultAiIntegrationCommand(
    Guid OrganizationId,
    string ProviderKey,
    string? Model)
    : IRequest<Result<OrganizationIntegrationResponse>>;
