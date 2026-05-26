namespace Incoders.Hive.Application.Abstractions.Observability;

/// <summary>
/// Insert-only repository for <see cref="UsageEvent"/> entries.
/// No updates or deletes — the ledger is append-only.
/// </summary>
public interface IUsageEventRepository
{
    Task AddAsync(UsageEvent evt, CancellationToken cancellationToken);
}
