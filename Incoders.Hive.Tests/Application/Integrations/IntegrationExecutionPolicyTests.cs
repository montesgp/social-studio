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
        var policy = new IntegrationExecutionPolicy(new ProviderFallbackPolicy(), new InMemoryAttemptRepository(), TimeProvider.System);
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
        var policy = new IntegrationExecutionPolicy(new ProviderFallbackPolicy(), new InMemoryAttemptRepository(), TimeProvider.System);

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
        var policy = new IntegrationExecutionPolicy(new ProviderFallbackPolicy(), new InMemoryAttemptRepository(), TimeProvider.System);
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

    private static IntegrationExecutionRequest Request() => new(
        OrganizationId,
        AnalysisId: Guid.NewGuid(),
        FlowKey: "single-analyzer",
        CapabilityKey: "ai-analysis",
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
}
