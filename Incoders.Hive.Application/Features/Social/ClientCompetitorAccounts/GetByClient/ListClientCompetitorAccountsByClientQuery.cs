using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientCompetitorAccounts.GetByClient;

public sealed record ListClientCompetitorAccountsByClientQuery(
    Guid OrganizationId,
    Guid ClientId,
    bool IncludeInactive,
    int Page,
    int PageSize) : IRequest<Result<PagedClientCompetitorAccountsResponse>>;
