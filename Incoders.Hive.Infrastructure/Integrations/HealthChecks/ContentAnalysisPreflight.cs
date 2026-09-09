using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Integrations.HealthChecks;

/// <summary>
/// Runs a real connectivity probe (the same checks as "Test connection") against each provider the
/// analyzer needs, before the workflow starts. Extraction is checked first, then AI.
/// </summary>
internal sealed class ContentAnalysisPreflight : IContentAnalysisPreflight
{
    private readonly IIntegrationResolver _integrationResolver;
    private readonly IReadOnlyList<IExternalIntegrationHealthCheck> _healthChecks;
    private readonly ILogger<ContentAnalysisPreflight> _logger;

    public ContentAnalysisPreflight(
        IIntegrationResolver integrationResolver,
        IEnumerable<IExternalIntegrationHealthCheck> healthChecks,
        ILogger<ContentAnalysisPreflight>? logger = null)
    {
        _integrationResolver = integrationResolver;
        _healthChecks = healthChecks.ToList();
        _logger = logger ?? NullLogger<ContentAnalysisPreflight>.Instance;
    }

    public async Task<Result> EnsureReadyAsync(
        Guid organizationId,
        string extractionProviderKey,
        string aiProviderKey,
        CancellationToken cancellationToken = default)
    {
        // MUST run sequentially: both probes resolve through IIntegrationResolver, which in
        // production is backed by a single scoped DbContext shared by the whole HTTP request.
        // Running them concurrently (previously Task.WhenAll) throws "A second operation was
        // started on this context instance..." under EF Core's concurrency detector — see
        // logs/hive-api.log 2026-09-09T20:50:17 / 21:50:27. Extraction keeps reporting priority
        // when both fail, checked first here.
        var extractionHealthy = await ProbeAsync(organizationId, extractionProviderKey, "extraction", cancellationToken);
        if (!extractionHealthy)
        {
            return Result.Failure(SingleAnalyzerErrors.ExtractionProviderUnavailable);
        }

        var aiHealthy = await ProbeAsync(organizationId, aiProviderKey, "ai", cancellationToken);
        if (!aiHealthy)
        {
            return Result.Failure(SingleAnalyzerErrors.AiProviderUnavailable);
        }

        return Result.Success();
    }

    private async Task<bool> ProbeAsync(
        Guid organizationId,
        string providerKey,
        string capability,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            _logger.LogWarning("Preflight: no {Capability} provider key for org {OrganizationId}.", capability, organizationId);
            return false;
        }

        var resolved = await _integrationResolver.ResolveProviderAsync(organizationId, providerKey, cancellationToken);
        if (resolved.IsFailure)
        {
            _logger.LogWarning(
                "Preflight: {Capability} provider {ProviderKey} not configured/connected for org {OrganizationId}: {ErrorCode}.",
                capability, providerKey, organizationId, resolved.Error.Code);
            return false;
        }

        var healthCheck = _healthChecks.FirstOrDefault(
            x => string.Equals(x.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase));

        // No live probe for this provider → treat "configured + connected" as ready.
        if (healthCheck is null)
        {
            return true;
        }

        var health = await healthCheck.CheckAsync(resolved.Value.Credentials, cancellationToken);
        if (!health.IsSuccess)
        {
            _logger.LogWarning(
                "Preflight: {Capability} provider {ProviderKey} unhealthy for org {OrganizationId}: status={StatusCode} code={ErrorCode} message={Message}.",
                capability, providerKey, organizationId, health.StatusCode, health.ErrorCode, health.Message);
            return false;
        }

        _logger.LogInformation(
            "Preflight: {Capability} provider {ProviderKey} healthy for org {OrganizationId}.",
            capability, providerKey, organizationId);
        return true;
    }
}
