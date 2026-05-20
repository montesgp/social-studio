using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientBrandManuals.Activate;

public sealed record ActivateClientBrandManualCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id) : IRequest<Result<ClientBrandManualResponse>>;
