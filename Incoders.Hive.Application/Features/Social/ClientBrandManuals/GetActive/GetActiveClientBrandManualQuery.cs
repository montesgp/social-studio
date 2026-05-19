using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientBrandManuals.GetActive;

public sealed record GetActiveClientBrandManualQuery(
    Guid OrganizationId,
    Guid ClientId) : IRequest<Result<ClientBrandManualResponse>>;
