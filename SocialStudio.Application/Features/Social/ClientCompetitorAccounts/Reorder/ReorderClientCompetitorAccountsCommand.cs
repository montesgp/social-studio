using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientCompetitorAccounts.Reorder;

public sealed record ReorderClientCompetitorAccountsCommand(
    Guid ClientId,
    Guid OrganizationId,
    IReadOnlyList<Guid> OrderedIds) : IRequest<Result<IReadOnlyList<ReorderedClientCompetitorAccountResponse>>>;
