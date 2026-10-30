using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Integrations;

namespace SocialStudio.Infrastructure.Integrations;

public sealed class DefaultIntegrationConnectionTester : IIntegrationConnectionTester
{
    private readonly IReadOnlyList<IExternalIntegrationHealthCheck> _healthChecks;

    public DefaultIntegrationConnectionTester(
        IEnumerable<IExternalIntegrationHealthCheck>? healthChecks = null)
    {
        _healthChecks = healthChecks?.ToList() ?? [];
    }

    public async Task<IntegrationConnectionTestResult> TestAsync(
        IntegrationProvider provider,
        IntegrationConnectionMode mode,
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken = default)
    {
        if (mode == IntegrationConnectionMode.SystemDefault)
        {
            return new IntegrationConnectionTestResult(true);
        }

        foreach (var field in provider.CredentialFields.Where(x => x.IsRequired))
        {
            if (!credentials.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return new IntegrationConnectionTestResult(false, $"Missing required credential: {field.Key}");
            }
        }

        // If a provider-specific health check exists, run a REAL connectivity probe against the
        // provider's API. Otherwise fall back to "required credentials present" (no live check yet).
        var healthCheck = _healthChecks.FirstOrDefault(
            x => string.Equals(x.ProviderKey, provider.Key, StringComparison.OrdinalIgnoreCase));

        if (healthCheck is null)
        {
            return new IntegrationConnectionTestResult(true);
        }

        var result = await healthCheck.CheckAsync(credentials, cancellationToken);
        return new IntegrationConnectionTestResult(
            result.IsSuccess,
            result.IsSuccess ? null : (result.Message ?? result.ErrorCode));
    }
}
