using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientCompetitorAccounts.Reorder;

public sealed record ReorderClientCompetitorAccountsCommand(
    Guid ClientId,
    Guid OrganizationId,
    IReadOnlyList<Guid> OrderedIds) : IRequest<Result<IReadOnlyList<ReorderedClientCompetitorAccountResponse>>>;
