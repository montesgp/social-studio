using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientReferentAccounts.GetByClient;

public sealed record ListClientReferentAccountsByClientQuery(
    Guid OrganizationId,
    Guid ClientId,
    bool IncludeInactive,
    int Page,
    int PageSize) : IRequest<Result<PagedClientReferentAccountsResponse>>;
