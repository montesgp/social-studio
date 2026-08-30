using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientOwnAccounts.Deactivate;

public sealed record DeactivateClientOwnAccountCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id) : IRequest<Result<ClientOwnAccountResponse>>;
