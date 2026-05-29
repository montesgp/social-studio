using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientCompetitorAccounts;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientCompetitorAccounts.Reorder;

public sealed class ReorderClientCompetitorAccountsCommandHandler
    : IRequestHandler<ReorderClientCompetitorAccountsCommand, Result<IReadOnlyList<ReorderedClientCompetitorAccountResponse>>>
{
    private readonly IClientCompetitorAccountRepository _accounts;
    private readonly ISocialClientRepository _clients;
    private readonly IClock _clock;

    public ReorderClientCompetitorAccountsCommandHandler(
        IClientCompetitorAccountRepository accounts,
        ISocialClientRepository clients,
        IClock clock)
    {
        _accounts = accounts;
        _clients = clients;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<ReorderedClientCompetitorAccountResponse>>> Handle(
        ReorderClientCompetitorAccountsCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<IReadOnlyList<ReorderedClientCompetitorAccountResponse>>(
                ClientCompetitorAccountErrors.NotFound);
        }

        if (request.OrderedIds is not { Count: > 0 })
        {
            return Result.Failure<IReadOnlyList<ReorderedClientCompetitorAccountResponse>>(
                ClientCompetitorAccountErrors.Validation("OrderedIds must not be empty."));
        }

        var allAccounts = await _accounts.GetAllByClientAsync(clientId, request.OrganizationId, cancellationToken);

        var existingIds = allAccounts.Select(a => a.Id.Value).ToHashSet();
        var suppliedIds = request.OrderedIds.ToHashSet();

        if (!existingIds.SetEquals(suppliedIds))
        {
            return Result.Failure<IReadOnlyList<ReorderedClientCompetitorAccountResponse>>(
                ClientCompetitorAccountErrors.Validation(
                    "The supplied account IDs do not match the client's current accounts exactly."));
        }

        var reorderResult = ClientCompetitorAccount.Reorder(
            allAccounts.ToList(), request.OrderedIds, _clock.UtcNow);

        if (reorderResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ReorderedClientCompetitorAccountResponse>>(reorderResult.Error);
        }

        await _accounts.ReorderAsync(clientId, request.OrganizationId, request.OrderedIds, cancellationToken);

        var response = reorderResult.Value
            .Select(ReorderedClientCompetitorAccountResponse.FromDomain)
            .ToList();

        return Result.Success<IReadOnlyList<ReorderedClientCompetitorAccountResponse>>(response);
    }
}
