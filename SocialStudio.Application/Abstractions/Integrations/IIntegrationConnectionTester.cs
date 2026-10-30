using SocialStudio.Domain.Integrations;

namespace SocialStudio.Application.Abstractions.Integrations;

public interface IIntegrationConnectionTester
{
    Task<IntegrationConnectionTestResult> TestAsync(
        IntegrationProvider provider,
        IntegrationConnectionMode mode,
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken = default);
}

public sealed record IntegrationConnectionTestResult(bool IsSuccess, string? ErrorMessage = null);