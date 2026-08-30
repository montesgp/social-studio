using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientOwnAccounts.Reorder;

public sealed record ReorderClientOwnAccountsCommand(
    Guid ClientId,
    Guid OrganizationId,
    IReadOnlyList<Guid> OrderedIds) : IRequest<Result<IReadOnlyList<ReorderedClientOwnAccountResponse>>>;
