using System.Diagnostics;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Infrastructure.Integrations;

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
                // Retry and fallback exist to survive a failing PROVIDER. Neither is a reason to keep
                // spending a budget the caller has already withdrawn, so the token is checked before
                // every attempt — including the first one of a fallback candidate.
                if (cancellationToken.IsCancellationRequested)
                {
                    // The provider is NOT called — but the abandonment is still recorded against this
                    // candidate. Incident 8757e85a: a job whose final state is missing from
                    // integration_execution_attempts is a job nobody can diagnose afterwards. The row
                    // is what makes "cancelled here" visible; skipping the call is what stops a
                    // badly-behaved adapter from burning its full timeout on work already withdrawn.
                    var cancelledAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
                    await PersistAttemptAsync(
                        request,
                        candidate,
                        IntegrationExecutionAttemptStatus.Failed,
                        attempt,
                        IntegrationErrors.OperationCancelled.Code,
                        retryCount: attempt - 1,
                        elapsedMs: 0,
                        diagnosticsJson: IntegrationErrors.OperationCancelled.MessageKey,
                        cancelledAtUtc,
                        completedAtUtc: cancelledAtUtc,
                        cancellationToken);

                    return CancelledExecution<TResponse>(request, diagnostics);
                }

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

                // Checked again here, not only at the loop top: the attempt itself may have been what
                // consumed the budget, and the retry path would otherwise reach a backoff delay that
                // throws on a cancelled token instead of returning the typed cancellation result.
                // No extra telemetry row here — the attempt that just ran was persisted above, and it
                // carries the real failure.
                if (cancellationToken.IsCancellationRequested)
                {
                    return CancelledExecution<TResponse>(request, diagnostics);
                }

                var decision = _fallbackPolicy.Decide(candidate, diagnostic);
                if (decision.ShouldRetry)
                {
                    // Back off before hitting the SAME provider again. Retrying instantly is useless
                    // against the failures we classify as retryable: a provider that is down, being
                    // restarted, or rate-limiting us needs wall-clock time to recover, and an
                    // immediate second call just burns the attempt budget on the same error.
                    await Task
                        .Delay(ResolveRetryBackoff(attempt), _timeProvider, cancellationToken)
                        .ConfigureAwait(false);
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

    /// <summary>
    /// Terminal result for a withdrawn caller. Distinct from
    /// <see cref="IntegrationErrors.ProviderTimeout"/> on purpose: the providers did not fail, so the
    /// diagnostics gathered so far describe attempts that are no longer worth acting on, and nothing
    /// downstream should read this as a reason to try again.
    /// </summary>
    private Result<ProviderExecutionResult<TResponse>> CancelledExecution<TResponse>(
        IntegrationExecutionRequest request,
        List<ProviderFailureDiagnostic> diagnostics)
        where TResponse : notnull
    {
        _logger.LogInformation(
            "Integration execution abandoned: the caller's token was cancelled. OrganizationId={OrganizationId} AnalysisId={AnalysisId} FlowKey={FlowKey} CapabilityKey={CapabilityKey} DiagnosticsCount={DiagnosticsCount}",
            request.OrganizationId,
            request.AnalysisId,
            request.FlowKey,
            request.CapabilityKey,
            diagnostics.Count);

        return Result.Failure<ProviderExecutionResult<TResponse>>(IntegrationErrors.OperationCancelled);
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

    /// <summary>
    /// Base delay for the first retry. Chosen against the failure this was built for: a provider
    /// refusing connections while it restarts fails in ~2s, so a comparable pause gives it a real
    /// chance to come back without noticeably lengthening a job that is already going to fail.
    /// </summary>
    internal static readonly TimeSpan RetryBackoffBase = TimeSpan.FromSeconds(2);

    /// <summary>Upper bound so a large attempt budget can never stall a job for minutes.</summary>
    internal static readonly TimeSpan RetryBackoffCap = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Exponential backoff for the retry that follows <paramref name="attempt"/>: 2s, 4s, 8s, …,
    /// capped at <see cref="RetryBackoffCap"/>. Pure and internal so the curve is unit-tested
    /// directly rather than inferred from wall-clock timing in a test.
    /// </summary>
    internal static TimeSpan ResolveRetryBackoff(int attempt)
    {
        if (attempt < 1)
        {
            return RetryBackoffBase;
        }

        // Shift instead of Math.Pow, and clamp the exponent so the doubling cannot overflow.
        var exponent = Math.Min(attempt - 1, 16);
        var delayTicks = RetryBackoffBase.Ticks * (1L << exponent);
        return delayTicks >= RetryBackoffCap.Ticks ? RetryBackoffCap : TimeSpan.FromTicks(delayTicks);
    }

    private static bool IsRetryable(string failureCode) =>
        failureCode.Contains("timeout", StringComparison.OrdinalIgnoreCase)
        || failureCode.Contains("rate_limited", StringComparison.OrdinalIgnoreCase)
        || failureCode.Contains("429", StringComparison.OrdinalIgnoreCase)
        || failureCode.Contains("5xx", StringComparison.OrdinalIgnoreCase)
        || failureCode.Contains("unavailable", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Bounded budget for the telemetry write when the caller's token is already cancelled. Kept
    /// short and deliberately NOT unbounded (<see cref="CancellationToken.None"/> with no time limit)
    /// so a stalled DB connection cannot hang job shutdown / cancellation indefinitely — the write is
    /// best-effort observability, not work the caller is waiting on.
    /// </summary>
    internal static readonly TimeSpan TelemetryWriteTimeout = TimeSpan.FromSeconds(5);

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

        // INCIDENT (job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1): when the caller's token is cancelled
        // (job timeout / host shutdown), writing with it throws OperationCanceledException BEFORE the
        // attempt row is saved — the four successful provider attempts of that job made it into
        // integration_execution_attempts, but the final FAILING attempt (the one that explains WHY
        // the job died) did not, because by then cancellationToken was already cancelled. The audit
        // trail lost exactly the record that would explain the failure. Substitute a short, bounded,
        // uncancelled token instead of the poisoned one — never CancellationToken.None with no bound,
        // which could let a stalled write hang shutdown forever.
        var writeToken = cancellationToken;
        CancellationTokenSource? boundedCts = null;
        if (cancellationToken.IsCancellationRequested)
        {
            boundedCts = new CancellationTokenSource(TelemetryWriteTimeout);
            writeToken = boundedCts.Token;
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
                writeToken);
        }
        catch (Exception ex)
        {
            // Best-effort: a telemetry failure (including the bounded write timing out) must never
            // fail the caller's actual work.
            _logger.LogWarning(
                ex,
                "Telemetry write failed — attempt record not persisted. OrganizationId={OrganizationId} AnalysisId={AnalysisId} CorrelationId={CorrelationId} CapabilityKey={CapabilityKey}",
                request.OrganizationId,
                request.AnalysisId,
                request.CorrelationId,
                request.CapabilityKey);
        }
        finally
        {
            boundedCts?.Dispose();
        }
    }
}
