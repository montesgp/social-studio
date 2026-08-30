using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Niches;

namespace Incoders.Hive.Tests.Application.Social.Niches;

/// <summary>
/// In-memory stand-in for the niche catalog port. Mirrors the ordering contract of the EF adapter:
/// a search term orders by display name, an empty term returns the most recently created first.
/// </summary>
internal sealed class InMemoryNicheRepository : INicheRepository
{
    private readonly Dictionary<NicheId, Niche> _store = new();

    public Task<Niche?> GetByIdAsync(NicheId id, CancellationToken cancellationToken)
    {
        _store.TryGetValue(id, out var niche);
        return Task.FromResult(niche);
    }

    public Task<Niche?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var match = _store.Values.FirstOrDefault(
            x => string.Equals(x.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match);
    }

    public Task<Niche?> GetByDisplayNameAsync(string displayName, CancellationToken cancellationToken)
    {
        var match = _store.Values.FirstOrDefault(
            x => string.Equals(x.DisplayName, displayName?.Trim(), StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match);
    }

    public Task<PageResult<Niche>> ListAsync(NicheListQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<Niche> source = _store.Values;

        if (!query.IncludeInactive)
        {
            source = source.Where(x => x.IsActive);
        }

        var term = query.SearchTerm?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
        {
            source = source.Where(x =>
                x.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Code.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = string.IsNullOrWhiteSpace(term)
            ? source.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.DisplayName).ToList()
            : source.OrderBy(x => x.DisplayName).ToList();

        var items = ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return Task.FromResult(new PageResult<Niche>(items, query.Page, query.PageSize, ordered.Count));
    }

    public Task<IReadOnlyList<Niche>> GetByIdsAsync(
        IReadOnlyCollection<NicheId> ids,
        CancellationToken cancellationToken)
    {
        var result = ids.Where(_store.ContainsKey).Select(id => _store[id]).ToList();
        return Task.FromResult<IReadOnlyList<Niche>>(result);
    }

    public Task AddAsync(Niche niche, CancellationToken cancellationToken)
    {
        _store[niche.Id] = niche;
        return Task.CompletedTask;
    }

    public void Update(Niche niche) => _store[niche.Id] = niche;

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>In-memory stand-in for the client ↔ niche association port.</summary>
internal sealed class InMemoryClientNicheRepository : IClientNicheRepository
{
    private readonly Dictionary<ClientId, List<NicheId>> _store = new();

    public Task<IReadOnlyList<NicheId>> GetNicheIdsAsync(ClientId clientId, CancellationToken cancellationToken)
    {
        var ids = _store.TryGetValue(clientId, out var list)
            ? list.ToList()
            : [];
        return Task.FromResult<IReadOnlyList<NicheId>>(ids);
    }

    public Task ReplaceAsync(
        ClientId clientId,
        IReadOnlyList<NicheId> nicheIds,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        _store[clientId] = nicheIds.ToList();
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
