using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientBrandManuals.GetById;

public sealed record GetClientBrandManualByIdQuery(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id) : IRequest<Result<ClientBrandManualResponse>>;
