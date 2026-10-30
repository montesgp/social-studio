using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientBrandManuals.Activate;

public sealed record ActivateClientBrandManualCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id) : IRequest<Result<ClientBrandManualResponse>>;
