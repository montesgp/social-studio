using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientReferentAccounts;
using SocialStudio.Domain.Social.Clients;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientReferentAccounts.Reorder;

public sealed class ReorderClientReferentAccountsCommandHandler
    : IRequestHandler<ReorderClientReferentAccountsCommand, Result<IReadOnlyList<ReorderedClientReferentAccountResponse>>>
{
    private readonly IClientReferentAccountRepository _accounts;
    private readonly ISocialClientRepository _clients;
    private readonly IClock _clock;

    public ReorderClientReferentAccountsCommandHandler(
        IClientReferentAccountRepository accounts,
        ISocialClientRepository clients,
        IClock clock)
    {
        _accounts = accounts;
        _clients = clients;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<ReorderedClientReferentAccountResponse>>> Handle(
        ReorderClientReferentAccountsCommand request,
        CancellationToken cancellationToken)
    {
        // Guard: client must exist in the tenant
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<IReadOnlyList<ReorderedClientReferentAccountResponse>>(
                ClientReferentAccountErrors.NotFound);
        }

        // Guard: ordered list must be non-empty (defense-in-depth — FluentValidation also catches this)
        if (request.OrderedIds is not { Count: > 0 })
        {
            return Result.Failure<IReadOnlyList<ReorderedClientReferentAccountResponse>>(
                ClientReferentAccountErrors.Validation("OrderedIds must not be empty."));
        }

        // Load all accounts for client (untracked for the domain reorder check)
        var allAccounts = await _accounts.GetAllByClientAsync(clientId, request.OrganizationId, cancellationToken);

        // Validate: supplied IDs must match the client's account IDs exactly (no additions, no omissions)
        var existingIds = allAccounts.Select(a => a.Id.Value).ToHashSet();
        var suppliedIds = request.OrderedIds.ToHashSet();

        if (!existingIds.SetEquals(suppliedIds))
        {
            return Result.Failure<IReadOnlyList<ReorderedClientReferentAccountResponse>>(
                ClientReferentAccountErrors.Validation(
                    "The supplied account IDs do not match the client's current accounts exactly."));
        }

        // Apply domain Reorder (mutates SortOrder on the in-memory account objects)
        var reorderResult = ClientReferentAccount.Reorder(
            allAccounts.ToList(), request.OrderedIds, _clock.UtcNow);

        if (reorderResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ReorderedClientReferentAccountResponse>>(reorderResult.Error);
        }

        // Persist the reordered accounts atomically via repo
        await _accounts.ReorderAsync(clientId, request.OrganizationId, request.OrderedIds, cancellationToken);

        var response = reorderResult.Value
            .Select(ReorderedClientReferentAccountResponse.FromDomain)
            .ToList();

        return Result.Success<IReadOnlyList<ReorderedClientReferentAccountResponse>>(response);
    }
}
