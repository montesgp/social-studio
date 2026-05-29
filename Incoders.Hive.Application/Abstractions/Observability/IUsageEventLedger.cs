namespace Incoders.Hive.Application.Abstractions.Observability;

/// <summary>
/// Writes one <see cref="UsageEvent"/> row per logical provider call.
/// Implementations MUST swallow all exceptions internally — a ledger write failure
/// must never propagate into the calling flow.
/// </summary>
public interface IUsageEventLedger
{
    /// <summary>
    /// Records a usage event. Guaranteed not to throw; on failure the implementation
    /// logs a warning and returns silently.
    /// </summary>
    Task RecordAsync(UsageEvent evt, CancellationToken cancellationToken);
}
