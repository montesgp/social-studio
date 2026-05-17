using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Infrastructure.Integrations;

public sealed class DefaultIntegrationConnectionTester : IIntegrationConnectionTester
{
    public Task<IntegrationConnectionTestResult> TestAsync(
        IntegrationProvider provider,
        IntegrationConnectionMode mode,
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        if (mode == IntegrationConnectionMode.SystemDefault)
        {
            return Task.FromResult(new IntegrationConnectionTestResult(true));
        }

        foreach (var field in provider.CredentialFields.Where(x => x.IsRequired))
        {
            if (!credentials.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return Task.FromResult(new IntegrationConnectionTestResult(
                    false,
                    $"Missing required credential: {field.Key}"));
            }
        }

        var hasFailureMarker = credentials.Values.Any(value =>
            value.Contains("__FAIL__", StringComparison.OrdinalIgnoreCase));

        if (hasFailureMarker)
        {
            return Task.FromResult(new IntegrationConnectionTestResult(
                false,
                "Credentials were rejected by provider test simulation."));
        }

        return Task.FromResult(new IntegrationConnectionTestResult(true));
    }
}