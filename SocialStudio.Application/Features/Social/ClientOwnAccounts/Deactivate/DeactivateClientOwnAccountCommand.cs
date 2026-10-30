using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientOwnAccounts.Deactivate;

public sealed record DeactivateClientOwnAccountCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id) : IRequest<Result<ClientOwnAccountResponse>>;
