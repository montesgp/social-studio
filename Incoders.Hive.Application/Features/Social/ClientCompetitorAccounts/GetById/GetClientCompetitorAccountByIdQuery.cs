using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientCompetitorAccounts.GetById;

public sealed record GetClientCompetitorAccountByIdQuery(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id) : IRequest<Result<ClientCompetitorAccountResponse>>;
