using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientBrandManuals.GetActive;

public sealed record GetActiveClientBrandManualQuery(
    Guid OrganizationId,
    Guid ClientId) : IRequest<Result<ClientBrandManualResponse>>;
