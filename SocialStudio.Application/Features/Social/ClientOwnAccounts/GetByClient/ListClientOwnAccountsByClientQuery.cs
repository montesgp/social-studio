using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientOwnAccounts.GetByClient;

public sealed record ListClientOwnAccountsByClientQuery(
    Guid OrganizationId,
    Guid ClientId,
    bool IncludeInactive,
    int Page,
    int PageSize) : IRequest<Result<PagedClientOwnAccountsResponse>>;
