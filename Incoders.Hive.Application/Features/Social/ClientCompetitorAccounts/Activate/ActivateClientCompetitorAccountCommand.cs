using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientCompetitorAccounts.Activate;

public sealed record ActivateClientCompetitorAccountCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id) : IRequest<Result<ClientCompetitorAccountResponse>>;
