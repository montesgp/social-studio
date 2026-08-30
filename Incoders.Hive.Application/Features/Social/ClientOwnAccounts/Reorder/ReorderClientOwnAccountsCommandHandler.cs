using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientOwnAccounts;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientOwnAccounts.Reorder;

public sealed class ReorderClientOwnAccountsCommandHandler
    : IRequestHandler<ReorderClientOwnAccountsCommand, Result<IReadOnlyList<ReorderedClientOwnAccountResponse>>>
{
    private readonly IClientOwnAccountRepository _accounts;
    private readonly ISocialClientRepository _clients;
    private readonly IClock _clock;

    public ReorderClientOwnAccountsCommandHandler(
        IClientOwnAccountRepository accounts,
        ISocialClientRepository clients,
        IClock clock)
    {
        _accounts = accounts;
        _clients = clients;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<ReorderedClientOwnAccountResponse>>> Handle(
        ReorderClientOwnAccountsCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<IReadOnlyList<ReorderedClientOwnAccountResponse>>(
                ClientOwnAccountErrors.NotFound);
        }

        if (request.OrderedIds is not { Count: > 0 })
        {
            return Result.Failure<IReadOnlyList<ReorderedClientOwnAccountResponse>>(
                ClientOwnAccountErrors.Validation("OrderedIds must not be empty."));
        }

        var allAccounts = await _accounts.GetAllByClientAsync(clientId, request.OrganizationId, cancellationToken);

        var existingIds = allAccounts.Select(a => a.Id.Value).ToHashSet();
        var suppliedIds = request.OrderedIds.ToHashSet();

        if (!existingIds.SetEquals(suppliedIds))
        {
            return Result.Failure<IReadOnlyList<ReorderedClientOwnAccountResponse>>(
                ClientOwnAccountErrors.Validation(
                    "The supplied account IDs do not match the client's current accounts exactly."));
        }

        var reorderResult = ClientOwnAccount.Reorder(
            allAccounts.ToList(), request.OrderedIds, _clock.UtcNow);

        if (reorderResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ReorderedClientOwnAccountResponse>>(reorderResult.Error);
        }

        await _accounts.ReorderAsync(clientId, request.OrganizationId, request.OrderedIds, cancellationToken);

        var response = reorderResult.Value
            .Select(ReorderedClientOwnAccountResponse.FromDomain)
            .ToList();

        return Result.Success<IReadOnlyList<ReorderedClientOwnAccountResponse>>(response);
    }
}
