using System.Diagnostics;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Integrations;

public sealed class IntegrationExecutionPolicy : IIntegrationExecutionPolicy
{
    private readonly IProviderFallbackPolicy _fallbackPolicy;
    private readonly IIntegrationExecutionAttemptRepository _attemptRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IntegrationExecutionPolicy> _logger;

    public IntegrationExecutionPolicy(
        IProviderFallbackPolicy fallbackPolicy,
        IIntegrationExecutionAttemptRepository attemptRepository,
        TimeProvider timeProvider,
        ILogger<IntegrationExecutionPolicy>? logger = null)
    {
        _fallbackPolicy = fallbackPolicy;
        _attemptRepository = attemptRepository;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<IntegrationExecutionPolicy>.Instance;
    }

    public async Task<Result<ProviderExecutionResult<TResponse>>> ExecuteAsync<TResponse>(
        IntegrationExecutionRequest request,
        IReadOnlyList<IntegrationRouteCandidate> candidates,
        Func<IntegrationRouteCandidate, CancellationToken, Task<Result<TResponse>>> executeAsync,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(executeAsync);

        var diagnostics = new List<ProviderFailureDiagnostic>();
        var enabledCandidates = candidates
            .Where(candidate => candidate.IsEnabled)
            .OrderBy(candidate => candidate.Priority)
            .ToArray();

        if (enabledCandidates.Length == 0)
        {
            return Result.Failure<ProviderExecutionResult<TResponse>>(IntegrationErrors.ProviderRouteNotConfigured);
        }

        foreach (var candidate in enabledCandidates)
        {
            for (var attempt = 1; attempt <= candidate.MaxAttempts; attempt++)
            {
                var startedAt = _timeProvider.GetUtcNow().UtcDateTime;
                var stopwatch = Stopwatch.StartNew();
                var result = await executeAsync(candidate, cancellationToken);
                stopwatch.Stop();

                if (result.IsSuccess)
                {
                    _logger.LogInformation(
                        "Integration provider attempt succeeded. OrganizationId={OrganizationId} AnalysisId={AnalysisId} FlowKey={FlowKey} CapabilityKey={CapabilityKey} PlatformKey={PlatformKey} ProviderKey={ProviderKey} Attempt={Attempt} ElapsedMs={ElapsedMs}",
                        request.OrganizationId,
                        request.AnalysisId,
                        request.FlowKey,
                        request.CapabilityKey,
                        request.PlatformKey,
                        candidate.ProviderKey,
                        attempt,
                        stopwatch.ElapsedMilliseconds);

                    await PersistAttemptAsync(
                        request,
                        candidate,
                        IntegrationExecutionAttemptStatus.Succeeded,
                        attempt,
                        failureCode: null,
                        retryCount: attempt - 1,
                        elapsedMs: (int)stopwatch.ElapsedMilliseconds,
                        diagnosticsJson: null,
                        startedAt,
                        completedAtUtc: _timeProvider.GetUtcNow().UtcDateTime,
                        cancellationToken);

                    return Result.Success(new ProviderExecutionResult<TResponse>(
                        result.Value,
                        candidate,
                        diagnostics));
                }

                var diagnostic = CreateDiagnostic(
                    request,
                    candidate,
                    attempt,
                    result.Error,
                    (int)stopwatch.ElapsedMilliseconds);
                diagnostics.Add(diagnostic);

                _logger.LogWarning(
                    "Integration provider attempt failed. OrganizationId={OrganizationId} AnalysisId={AnalysisId} FlowKey={FlowKey} CapabilityKey={CapabilityKey} PlatformKey={PlatformKey} ProviderKey={ProviderKey} Attempt={Attempt} FailureCode={FailureCode} MessageKey={MessageKey} Retryable={Retryable} ElapsedMs={ElapsedMs}",
                    request.OrganizationId,
                    request.AnalysisId,
                    request.FlowKey,
                    request.CapabilityKey,
                    request.PlatformKey,
                    candidate.ProviderKey,
                    attempt,
                    diagnostic.FailureCode,
                    diagnostic.MessageKey,
                    diagnostic.IsRetryable,
                    diagnostic.ElapsedMs);

                await PersistAttemptAsync(
                    request,
                    candidate,
                    IntegrationExecutionAttemptStatus.Failed,
                    attempt,
                    diagnostic.FailureCode,
                    retryCount: attempt - 1,
                    diagnostic.ElapsedMs,
                    diagnosticsJson: diagnostic.MessageKey,
                    startedAt,
                    completedAtUtc: diagnostic.OccurredAtUtc,
                    cancellationToken);

                var decision = _fallbackPolicy.Decide(candidate, diagnostic);
                if (decision.ShouldRetry)
                {
                    continue;
                }

                if (decision.ShouldFallback)
                {
                    break;
                }

                return Result.Failure<ProviderExecutionResult<TResponse>>(result.Error);
            }
        }

        var finalError = diagnostics.Count > 0
            ? Error.Failure(diagnostics[^1].FailureCode, diagnostics[^1].MessageKey)
            : IntegrationErrors.ExternalProviderUnavailable;

        _logger.LogError(
            "Integration execution exhausted all candidates. OrganizationId={OrganizationId} AnalysisId={AnalysisId} FlowKey={FlowKey} CapabilityKey={CapabilityKey} PlatformKey={PlatformKey} FinalFailureCode={FailureCode} MessageKey={MessageKey} DiagnosticsCount={DiagnosticsCount}",
            request.OrganizationId,
            request.AnalysisId,
            request.FlowKey,
            request.CapabilityKey,
            request.PlatformKey,
            finalError.Code,
            finalError.MessageKey,
            diagnostics.Count);

        return Result.Failure<ProviderExecutionResult<TResponse>>(finalError);
    }

    private ProviderFailureDiagnostic CreateDiagnostic(
        IntegrationExecutionRequest request,
        IntegrationRouteCandidate candidate,
        int attempt,
        Error error,
        int elapsedMs)
    {
        var isRetryable = IsRetryable(error.Code);
        return ProviderFailureDiagnostic.Create(
            request.CapabilityKey,
            candidate.ProviderKey,
            attempt,
            error.Code,
            error.MessageKey,
            isRetryable,
            fallbackAllowed: true,
            elapsedMs,
            _timeProvider.GetUtcNow().UtcDateTime).Value;
    }

    private static bool IsRetryable(string failureCode) =>
        failureCode.Contains("timeout", StringComparison.OrdinalIgnoreCase)
        || failureCode.Contains("rate_limited", StringComparison.OrdinalIgnoreCase)
        || failureCode.Contains("429", StringComparison.OrdinalIgnoreCase)
        || failureCode.Contains("5xx", StringComparison.OrdinalIgnoreCase)
        || failureCode.Contains("unavailable", StringComparison.OrdinalIgnoreCase);

    private async Task PersistAttemptAsync(
        IntegrationExecutionRequest request,
        IntegrationRouteCandidate candidate,
        IntegrationExecutionAttemptStatus status,
        int attemptNumber,
        string? failureCode,
        int retryCount,
        int elapsedMs,
        string? diagnosticsJson,
        DateTime startedAtUtc,
        DateTime? completedAtUtc,
        CancellationToken cancellationToken)
    {
        // Skip telemetry only when neither anchor is present.
        if (request.AnalysisId is null && request.CorrelationId is null)
        {
            return;
        }

        try
        {
            await _attemptRepository.AddAsync(
                new IntegrationExecutionAttemptRecord(
                    Guid.NewGuid(),
                    request.AnalysisId,
                    request.CapabilityKey,
                    candidate.ProviderKey,
                    status,
                    attemptNumber,
                    failureCode,
                    retryCount,
                    elapsedMs,
                    diagnosticsJson,
                    startedAtUtc,
                    completedAtUtc,
                    CorrelationId: request.CorrelationId,
                    CorrelationKind: request.CorrelationKind),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Telemetry write failed — attempt record not persisted. OrganizationId={OrganizationId} AnalysisId={AnalysisId} CorrelationId={CorrelationId} CapabilityKey={CapabilityKey}",
                request.OrganizationId,
                request.AnalysisId,
                request.CorrelationId,
                request.CapabilityKey);
        }
    }
}
