using System.Collections.Concurrent;

namespace SocialStudio.Infrastructure.Identity;

/// <summary>
/// In-memory refresh-token registry. Replace with a persistent store (EF Core table, Redis) for production.
/// </summary>
internal sealed class RefreshTokenStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public void Store(string token, Guid userId, DateTime expiresAtUtc)
    {
        _entries[token] = new Entry(userId, expiresAtUtc);
    }

    public Guid? Consume(string token, DateTime nowUtc)
    {
        if (!_entries.TryRemove(token, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAtUtc <= nowUtc)
        {
            return null;
        }

        return entry.UserId;
    }

    private sealed record Entry(Guid UserId, DateTime ExpiresAtUtc);
}
