using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;

namespace Incoders.Hive.Tests.Application.Integrations;

public sealed class IntegrationExecutionPolicyTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    [Fact]
    public async Task ExecuteAsync_RetriesRetryableFailureBeforeReturningSuccess()
    {
        var candidate = Candidate("openai", maxAttempts: 2);
        var policy = new IntegrationExecutionPolicy(new ProviderFallbackPolicy(), new InMemoryAttemptRepository(), new ImmediateTimeProvider());
        var calls = 0;

        var result = await policy.ExecuteAsync(
            Request(),
            [candidate],
            (_, _) =>
            {
                calls++;
                return Task.FromResult(calls == 1
                    ? Result.Failure<string>(IntegrationErrors.ProviderTimeout)
                    : Result.Success("ok"));
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value.Payload);
        Assert.Equal(2, calls);
        Assert.Single(result.Value.Diagnostics);
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToNextCandidateAfterRetryExhausted()
    {
        var first = Candidate("openai", priority: 10, maxAttempts: 1);
        var second = Candidate("claude", priority: 20, maxAttempts: 1);
        var policy = new IntegrationExecutionPolicy(new ProviderFallbackPolicy(), new InMemoryAttemptRepository(), new ImmediateTimeProvider());

        var result = await policy.ExecuteAsync(
            Request(),
            [first, second],
            (candidate, _) => Task.FromResult(candidate.ProviderKey == "openai"
                ? Result.Failure<string>(IntegrationErrors.ProviderTimeout)
                : Result.Success("fallback-ok")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("fallback-ok", result.Value.Payload);
        Assert.Equal("claude", result.Value.Candidate.ProviderKey);
        Assert.Contains(result.Value.Diagnostics, diagnostic => diagnostic.ProviderKey == "openai");
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRetryCredentialFailures()
    {
        var candidate = Candidate("openai", maxAttempts: 3);
        var policy = new IntegrationExecutionPolicy(new ProviderFallbackPolicy(), new InMemoryAttemptRepository(), new ImmediateTimeProvider());
        var calls = 0;

        var result = await policy.ExecuteAsync(
            Request(),
            [candidate],
            (_, _) =>
            {
                calls++;
                return Task.FromResult(Result.Failure<string>(IntegrationErrors.ProviderCredentialUnavailable));
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, calls);
    }

    // 6.3 RED → 6.4 GREEN: CorrelationId-only anchor persists an attempt row.
    [Fact]
    public async Task ExecuteAsync_CorrelationIdOnly_AttemptPersisted()
    {
        var repository = new InMemoryAttemptRepository();
        var policy = new IntegrationExecutionPolicy(new ProviderFallbackPolicy(), repository, new ImmediateTimeProvider());

        var result = await policy.ExecuteAsync(
            CorrelationOnlyRequest(),
            [Candidate("openai", maxAttempts: 1)],
            (_, _) => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(repository.Attempts);
        Assert.Null(repository.Attempts[0].AnalysisId);
        Assert.NotNull(repository.Attempts[0].CorrelationId);
    }

    // 6.3 RED → 6.4 GREEN: both anchors null → attempt NOT persisted.
    [Fact]
    public async Task ExecuteAsync_BothAnchorsNull_AttemptNotPersisted()
    {
        var repository = new InMemoryAttemptRepository();
        var policy = new IntegrationExecutionPolicy(new ProviderFallbackPolicy(), repository, new ImmediateTimeProvider());

        var result = await policy.ExecuteAsync(
            NoAnchorRequest(),
            [Candidate("openai", maxAttempts: 1)],
            (_, _) => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(repository.Attempts);
    }

    // 6.3 RED → 6.4 GREEN: repository write failure is caught — generation job succeeds.
    [Fact]
    public async Task ExecuteAsync_PersistenceWriteThrows_JobStillSucceeds()
    {
        var policy = new IntegrationExecutionPolicy(new ProviderFallbackPolicy(), new FailingAttemptRepository(), new ImmediateTimeProvider());

        var result = await policy.ExecuteAsync(
            Request(),
            [Candidate("openai", maxAttempts: 1)],
            (_, _) => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value.Payload);
    }

    // Regression: job 5d2c7b35 lost its AI backgrounds because ComfyUI was briefly unreachable and
    // the default route candidate carried MaxAttempts = 1, which made ProviderFallbackPolicy's
    // `AttemptNumber < MaxAttempts` guard false — a retryable failure was never retried.
    [Fact]
    public void DefaultRouteCandidate_AllowsAtLeastOneRetry()
    {
        var candidate = new WorkflowStepRouteCandidate(
            ProviderId: Guid.NewGuid(),
            ProviderKey: "comfyui",
            OrganizationIntegrationId: Guid.NewGuid(),
            ConnectionMode: IntegrationConnectionMode.ManagedByIncoders,
            Priority: 1,
            ModelId: null,
            ActorId: null,
            IsEnabled: true).ToIntegrationRouteCandidate();

        Assert.True(candidate.MaxAttempts > 1, "a MaxAttempts of 1 silently disables all retries");
    }

    [Fact]
    public async Task ExecuteAsync_ProviderUnavailable_IsRetried()
    {
        var candidate = Candidate("comfyui", maxAttempts: 2);
        var policy = new IntegrationExecutionPolicy(
            new ProviderFallbackPolicy(), new InMemoryAttemptRepository(), new ImmediateTimeProvider());
        var calls = 0;

        var result = await policy.ExecuteAsync(
            Request(),
            [candidate],
            (_, _) =>
            {
                calls++;
                return Task.FromResult(calls == 1
                    ? Result.Failure<string>(IntegrationErrors.ExternalProviderUnavailable)
                    : Result.Success("recovered"));
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("recovered", result.Value.Payload);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ExecuteAsync_WaitsBeforeRetryingTheSameProvider()
    {
        // Retrying instantly against a provider that is down just burns the attempt budget on the
        // same error, so a retry MUST be preceded by a real delay.
        var timeProvider = new ImmediateTimeProvider();
        var policy = new IntegrationExecutionPolicy(
            new ProviderFallbackPolicy(), new InMemoryAttemptRepository(), timeProvider);
        var calls = 0;

        await policy.ExecuteAsync(
            Request(),
            [Candidate("comfyui", maxAttempts: 2)],
            (_, _) =>
            {
                calls++;
                return Task.FromResult(calls == 1
                    ? Result.Failure<string>(IntegrationErrors.ExternalProviderUnavailable)
                    : Result.Success("recovered"));
            },
            CancellationToken.None);

        Assert.Single(timeProvider.RequestedDelays);
        Assert.Equal(IntegrationExecutionPolicy.RetryBackoffBase, timeProvider.RequestedDelays[0]);
    }

    [Fact]
    public async Task ExecuteAsync_SucceedsFirstTime_DoesNotWait()
    {
        var timeProvider = new ImmediateTimeProvider();
        var policy = new IntegrationExecutionPolicy(
            new ProviderFallbackPolicy(), new InMemoryAttemptRepository(), timeProvider);

        await policy.ExecuteAsync(
            Request(),
            [Candidate("comfyui", maxAttempts: 2)],
            (_, _) => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        Assert.Empty(timeProvider.RequestedDelays);
    }

    [Fact]
    public void ResolveRetryBackoff_DoublesPerAttemptAndIsCapped()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), IntegrationExecutionPolicy.ResolveRetryBackoff(1));
        Assert.Equal(TimeSpan.FromSeconds(4), IntegrationExecutionPolicy.ResolveRetryBackoff(2));
        Assert.Equal(TimeSpan.FromSeconds(8), IntegrationExecutionPolicy.ResolveRetryBackoff(3));

        // Cap holds for any large attempt number, and the doubling never overflows.
        Assert.Equal(IntegrationExecutionPolicy.RetryBackoffCap, IntegrationExecutionPolicy.ResolveRetryBackoff(20));
        Assert.Equal(IntegrationExecutionPolicy.RetryBackoffCap, IntegrationExecutionPolicy.ResolveRetryBackoff(int.MaxValue));
    }

    // Change 2 (incident job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1): PersistAttemptAsync used to write
    // telemetry with the caller's own CancellationToken. When the job's token was already cancelled
    // (timeout/shutdown), that write threw OperationCanceledException BEFORE saving — the four
    // successful attempts of that job made it into integration_execution_attempts, but the final
    // FAILING attempt (the one that explains WHY the job died) did not. Assert the write survives an
    // already-cancelled token, using a token that is guaranteed not to be the poisoned one.
    [Fact]
    public async Task ExecuteAsync_CallerTokenAlreadyCancelled_AttemptStillPersisted()
    {
        var repository = new TokenCapturingAttemptRepository();
        var policy = new IntegrationExecutionPolicy(new ProviderFallbackPolicy(), repository, new ImmediateTimeProvider());

        using var alreadyCancelled = new CancellationTokenSource();
        alreadyCancelled.Cancel();

        var result = await policy.ExecuteAsync(
            CorrelationOnlyRequest(),
            [Candidate("openai", maxAttempts: 1)],
            // The candidate call itself is expected to observe cancellation and fail fast — mirrors
            // what happens when a job timeout fires mid in-flight provider call.
            (_, ct) => Task.FromResult(ct.IsCancellationRequested
                ? Result.Failure<string>(IntegrationErrors.ProviderTimeout)
                : Result.Success("unexpected")),
            alreadyCancelled.Token);

        Assert.True(result.IsFailure);
        Assert.Single(repository.Attempts);
        Assert.Equal(IntegrationExecutionAttemptStatus.Failed, repository.Attempts[0].Status);

        // The write itself must NOT have used the already-cancelled token — otherwise AddAsync would
        // have thrown before the record was captured at all.
        Assert.NotNull(repository.LastWriteToken);
        Assert.False(repository.LastWriteToken!.Value.IsCancellationRequested);
    }

    [Fact]
    public void TelemetryWriteTimeout_IsBoundedNotUnbounded()
    {
        // Guards the explicit instruction: never substitute CancellationToken.None with no time bound
        // for the resilient write — a wedged DB connection must not hang job shutdown forever.
        Assert.True(IntegrationExecutionPolicy.TelemetryWriteTimeout > TimeSpan.Zero);
        Assert.True(IntegrationExecutionPolicy.TelemetryWriteTimeout < TimeSpan.FromMinutes(1));
    }

    // -----------------------------------------------------------------------
    // The caller's token is the authority on whether the work is still wanted. Retry and fallback
    // are recovery strategies for a failing PROVIDER; neither is a reason to keep spending a budget
    // the caller has already withdrawn. Before this guard the loop only ever observed cancellation
    // through the backoff delay, so an exhausted attempt — which has no delay after it — walked
    // straight on to the next candidate.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_TokenAlreadyCancelled_DoesNotCallTheProvider()
    {
        var candidate = Candidate("openai", maxAttempts: 2);
        var policy = new IntegrationExecutionPolicy(new ProviderFallbackPolicy(), new InMemoryAttemptRepository(), new ImmediateTimeProvider());
        var calls = 0;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await policy.ExecuteAsync(
            Request(),
            [candidate],
            (_, _) =>
            {
                calls++;
                return Task.FromResult(Result.Success("ok"));
            },
            cts.Token);

        Assert.Equal(0, calls);
        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.OperationCancelled.Code, result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_TokenCancelledDuringAnAttempt_DoesNotRetryTheSameProvider()
    {
        var candidate = Candidate("openai", maxAttempts: 3);
        var policy = new IntegrationExecutionPolicy(new ProviderFallbackPolicy(), new InMemoryAttemptRepository(), new ImmediateTimeProvider());
        var calls = 0;
        using var cts = new CancellationTokenSource();

        var result = await policy.ExecuteAsync(
            Request(),
            [candidate],
            (_, _) =>
            {
                calls++;
                cts.Cancel();
                return Task.FromResult(Result.Failure<string>(IntegrationErrors.ProviderTimeout));
            },
            cts.Token);

        Assert.Equal(1, calls);
        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.OperationCancelled.Code, result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_TokenCancelledDuringAnAttempt_DoesNotFallBackToTheNextCandidate()
    {
        var first = Candidate("openai", priority: 10, maxAttempts: 1);
        var second = Candidate("claude", priority: 20, maxAttempts: 1);
        var policy = new IntegrationExecutionPolicy(new ProviderFallbackPolicy(), new InMemoryAttemptRepository(), new ImmediateTimeProvider());
        var providersCalled = new List<string>();
        using var cts = new CancellationTokenSource();

        var result = await policy.ExecuteAsync(
            Request(),
            [first, second],
            (candidate, _) =>
            {
                providersCalled.Add(candidate.ProviderKey);
                cts.Cancel();
                return Task.FromResult(Result.Failure<string>(IntegrationErrors.ProviderTimeout));
            },
            cts.Token);

        Assert.Equal(["openai"], providersCalled);
        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.OperationCancelled.Code, result.Error.Code);
    }

    [Fact]
    public void OperationCancelled_IsNotClassifiedAsRetryable()
    {
        // IsRetryable matches on substrings of the failure code, so a cancellation code that
        // happened to contain "timeout" or "unavailable" would silently be retried.
        Assert.DoesNotContain("timeout", IntegrationErrors.OperationCancelled.Code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unavailable", IntegrationErrors.OperationCancelled.Code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rate_limited", IntegrationErrors.OperationCancelled.Code, StringComparison.OrdinalIgnoreCase);
    }

    private static IntegrationExecutionRequest Request() => new(
        OrganizationId,
        AnalysisId: Guid.NewGuid(),
        FlowKey: "single-analyzer",
        CapabilityKey: "ai-analysis",
        PlatformKey: null);

    private static IntegrationExecutionRequest CorrelationOnlyRequest() => new(
        OrganizationId,
        AnalysisId: null,
        FlowKey: "single-analyzer",
        CapabilityKey: "generate-image",
        PlatformKey: null,
        CorrelationId: Guid.NewGuid(),
        CorrelationKind: "content-generation/generate-image");

    private static IntegrationExecutionRequest NoAnchorRequest() => new(
        OrganizationId,
        AnalysisId: null,
        FlowKey: "single-analyzer",
        CapabilityKey: "generate-image",
        PlatformKey: null);

    private static IntegrationRouteCandidate Candidate(
        string providerKey,
        int priority = 10,
        int maxAttempts = 2) =>
        IntegrationRouteCandidate.Create(
            Guid.NewGuid(),
            providerKey,
            IntegrationConnectionMode.BringYourOwnKey,
            priority,
            allowManagedFallback: true,
            maxAttempts,
            timeoutMs: 2_000,
            selectedModel: null,
            isEnabled: true).Value;

    /// <summary>
    /// TimeProvider whose timers fire immediately, so <c>TimeProvider.Delay</c> — and therefore the
    /// policy's retry backoff — completes without real wall-clock waiting. Overriding CreateTimer is
    /// what makes this work: Delay is built on top of it. Without this the retry tests would sleep
    /// for the full backoff on every run.
    /// </summary>
    private sealed class ImmediateTimeProvider : TimeProvider
    {
        public List<TimeSpan> RequestedDelays { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime > TimeSpan.Zero)
            {
                RequestedDelays.Add(dueTime);
            }

            return new ImmediateTimer(callback, state);
        }

        private sealed class ImmediateTimer : ITimer
        {
            public ImmediateTimer(TimerCallback callback, object? state) => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class InMemoryAttemptRepository : IIntegrationExecutionAttemptRepository
    {
        public List<IntegrationExecutionAttemptRecord> Attempts { get; } = [];

        public Task AddAsync(IntegrationExecutionAttemptRecord attempt, CancellationToken cancellationToken = default)
        {
            Attempts.Add(attempt);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IntegrationExecutionAttemptRecord>> ListByAnalysisIdAsync(
            Guid analysisId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IntegrationExecutionAttemptRecord>>(
                Attempts.Where(x => x.AnalysisId == analysisId).ToArray());
    }

    /// <summary>Captures the CancellationToken actually passed to AddAsync, so the test can assert
    /// it is NOT the caller's already-cancelled token (Change 2).</summary>
    private sealed class TokenCapturingAttemptRepository : IIntegrationExecutionAttemptRepository
    {
        public List<IntegrationExecutionAttemptRecord> Attempts { get; } = [];

        public CancellationToken? LastWriteToken { get; private set; }

        public Task AddAsync(IntegrationExecutionAttemptRecord attempt, CancellationToken cancellationToken = default)
        {
            LastWriteToken = cancellationToken;
            Attempts.Add(attempt);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IntegrationExecutionAttemptRecord>> ListByAnalysisIdAsync(
            Guid analysisId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IntegrationExecutionAttemptRecord>>(
                Attempts.Where(x => x.AnalysisId == analysisId).ToArray());
    }

    private sealed class FailingAttemptRepository : IIntegrationExecutionAttemptRepository
    {
        public Task AddAsync(IntegrationExecutionAttemptRecord attempt, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated persistence failure");

        public Task<IReadOnlyList<IntegrationExecutionAttemptRecord>> ListByAnalysisIdAsync(
            Guid analysisId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IntegrationExecutionAttemptRecord>>(Array.Empty<IntegrationExecutionAttemptRecord>());
    }
}
