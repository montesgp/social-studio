using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Reorder;

public sealed record ReorderClientReferentAccountsCommand(
    Guid ClientId,
    Guid OrganizationId,
    IReadOnlyList<Guid> OrderedIds) : IRequest<Result<IReadOnlyList<ReorderedClientReferentAccountResponse>>>;
