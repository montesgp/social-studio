namespace Incoders.Hive.Application.Abstractions.Integrations;

/// <summary>
/// Performs a real, lightweight connectivity check against an external provider's API using the
/// organization's credentials (e.g. a cheap GET). One implementation per provider key.
/// </summary>
public interface IExternalIntegrationHealthCheck
{
    /// <summary>Provider key this health check handles (e.g. "apify", "claude").</summary>
    string ProviderKey { get; }

    Task<IntegrationHealthResult> CheckAsync(
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken = default);
}

public sealed record IntegrationHealthResult(
    bool IsSuccess,
    string Provider,
    int? StatusCode,
    string? ErrorCode,
    string? Message);
