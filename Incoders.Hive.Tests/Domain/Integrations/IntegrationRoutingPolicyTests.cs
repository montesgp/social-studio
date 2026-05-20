using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Tests.Domain.Integrations;

public sealed class IntegrationRoutingPolicyTests
{
    private static readonly DateTime Now = new(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_OrdersEnabledCandidatesByPersistedPriority()
    {
        var firstProvider = Guid.NewGuid();
        var secondProvider = Guid.NewGuid();

        var policy = IntegrationRoutingPolicy.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            " single-analyzer ",
            " social-extraction ",
            " TikTok ",
            isEnabled: true,
            [
                IntegrationRouteCandidate.Create(
                    firstProvider,
                    "apify",
                    IntegrationConnectionMode.BringYourOwnKey,
                    priority: 20,
                    allowManagedFallback: true,
                    maxAttempts: 2,
                    timeoutMs: 5_000,
                    selectedModel: null,
                    isEnabled: true).Value,
                IntegrationRouteCandidate.Create(
                    secondProvider,
                    "data365",
                    IntegrationConnectionMode.BringYourOwnKey,
                    priority: 10,
                    allowManagedFallback: false,
                    maxAttempts: 1,
                    timeoutMs: 3_000,
                    selectedModel: null,
                    isEnabled: true).Value
            ],
            Now);

        Assert.True(policy.IsSuccess);
        Assert.Equal("single-analyzer", policy.Value.FlowKey);
        Assert.Equal("social-extraction", policy.Value.CapabilityKey);
        Assert.Equal("tiktok", policy.Value.PlatformKey);
        Assert.Equal(["data365", "apify"], policy.Value.GetEnabledCandidates().Select(x => x.ProviderKey).ToArray());
    }

    [Fact]
    public void Create_RejectsDuplicateEnabledPriorities()
    {
        var duplicatePriority = 10;

        var policy = IntegrationRoutingPolicy.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "single-analyzer",
            "ai-analysis",
            platformKey: null,
            isEnabled: true,
            [
                Candidate("openai", duplicatePriority),
                Candidate("claude", duplicatePriority)
            ],
            Now);

        Assert.True(policy.IsFailure);
        Assert.Equal("integrations.routing_policy_duplicate_priority", policy.Error.Code);
    }

    [Fact]
    public void Create_RequiresAnEnabledCandidateWhenPolicyIsEnabled()
    {
        var disabledCandidate = IntegrationRouteCandidate.Create(
            Guid.NewGuid(),
            "openai",
            IntegrationConnectionMode.BringYourOwnKey,
            priority: 10,
            allowManagedFallback: false,
            maxAttempts: 1,
            timeoutMs: 2_000,
            selectedModel: "gpt-4.1-mini",
            isEnabled: false).Value;

        var policy = IntegrationRoutingPolicy.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "single-analyzer",
            "ai-analysis",
            platformKey: null,
            isEnabled: true,
            [disabledCandidate],
            Now);

        Assert.True(policy.IsFailure);
        Assert.Equal("integrations.routing_policy_requires_enabled_candidate", policy.Error.Code);
    }

    [Fact]
    public void Candidate_RejectsManagedFallbackWhenPolicyItemDisallowsIt()
    {
        var candidate = IntegrationRouteCandidate.Create(
            Guid.NewGuid(),
            "openai",
            IntegrationConnectionMode.ManagedByIncoders,
            priority: 10,
            allowManagedFallback: false,
            maxAttempts: 1,
            timeoutMs: 2_000,
            selectedModel: null,
            isEnabled: true);

        Assert.True(candidate.IsFailure);
        Assert.Equal("integrations.routing_candidate_managed_fallback_not_allowed", candidate.Error.Code);
    }

    [Fact]
    public void ProviderFailureDiagnostic_NormalizesProviderFailureWithoutCredentialPayload()
    {
        var diagnostic = ProviderFailureDiagnostic.Create(
            " ai-analysis ",
            " OpenAI ",
            attemptNumber: 2,
            failureCode: " Timeout ",
            messageKey: " integrations.failures.timeout ",
            isRetryable: true,
            fallbackAllowed: true,
            elapsedMs: 750,
            occurredAtUtc: Now);

        Assert.True(diagnostic.IsSuccess);
        Assert.Equal("ai-analysis", diagnostic.Value.CapabilityKey);
        Assert.Equal("openai", diagnostic.Value.ProviderKey);
        Assert.Equal("timeout", diagnostic.Value.FailureCode);
        Assert.Equal(2, diagnostic.Value.AttemptNumber);
        Assert.True(diagnostic.Value.IsRetryable);
        Assert.True(diagnostic.Value.FallbackAllowed);
        Assert.Equal(750, diagnostic.Value.ElapsedMs);
    }

    private static IntegrationRouteCandidate Candidate(string providerKey, int priority) =>
        IntegrationRouteCandidate.Create(
            Guid.NewGuid(),
            providerKey,
            IntegrationConnectionMode.BringYourOwnKey,
            priority,
            allowManagedFallback: false,
            maxAttempts: 1,
            timeoutMs: 2_000,
            selectedModel: null,
            isEnabled: true).Value;
}