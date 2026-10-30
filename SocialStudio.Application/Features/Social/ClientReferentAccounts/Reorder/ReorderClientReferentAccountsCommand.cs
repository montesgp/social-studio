using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientReferentAccounts.Reorder;

public sealed record ReorderClientReferentAccountsCommand(
    Guid ClientId,
    Guid OrganizationId,
    IReadOnlyList<Guid> OrderedIds) : IRequest<Result<IReadOnlyList<ReorderedClientReferentAccountResponse>>>;
