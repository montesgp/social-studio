using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Update;

public sealed record UpdateClientReferentAccountCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes) : IRequest<Result<ClientReferentAccountResponse>>;
