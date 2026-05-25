using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;

namespace Incoders.Hive.Tests.Infrastructure.Social;

/// <summary>
/// Unit tests for DefaultAiSelector — the Infrastructure adapter that wraps
/// IIntegrationResolver.ResolveDefaultAiProviderAsync and maps it to DefaultAiSelection.
/// </summary>
public sealed class DefaultAiSelectorTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    // ─── Happy path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SelectAsync_ReturnsProviderKeyAndModel_WhenDefaultAiResolves()
    {
        var resolver = new FakeIntegrationResolver(
            Result.Success(ResolvedAi("openai", "gpt-4o")));
        var selector = new DefaultAiSelector(resolver);

        var result = await selector.SelectAsync(OrgId);

        Assert.True(result.IsSuccess);
        Assert.Equal("openai", result.Value.ProviderKey);
        Assert.Equal("gpt-4o", result.Value.Model);
    }

    [Fact]
    public async Task SelectAsync_ReturnsProviderKey_WhenModelIsNull()
    {
        var resolver = new FakeIntegrationResolver(
            Result.Success(ResolvedAi("anthropic", null)));
        var selector = new DefaultAiSelector(resolver);

        var result = await selector.SelectAsync(OrgId);

        Assert.True(result.IsSuccess);
        Assert.Equal("anthropic", result.Value.ProviderKey);
        Assert.Null(result.Value.Model);
    }

    // ─── Failure propagation ─────────────────────────────────────────────────

    [Fact]
    public async Task SelectAsync_PropagatesFailure_WhenNoAiProviderConfigured()
    {
        var resolver = new FakeIntegrationResolver(
            Result.Failure<ResolvedIntegration>(IntegrationErrors.DefaultAiProviderNotConnected));
        var selector = new DefaultAiSelector(resolver);

        var result = await selector.SelectAsync(OrgId);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.DefaultAiProviderNotConnected.Code, result.Error.Code);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ResolvedIntegration ResolvedAi(string providerKey, string? model) =>
        new(OrgId, providerKey, "AI Provider", IntegrationProviderCategory.AiModel,
            IntegrationConnectionMode.BringYourOwnKey, IntegrationStatus.Connected,
            IsReadOnly: false, IsDefaultAi: true, model,
            new Dictionary<string, string>());

    private sealed class FakeIntegrationResolver : IIntegrationResolver
    {
        private readonly Result<ResolvedIntegration> _defaultAiResult;

        public FakeIntegrationResolver(Result<ResolvedIntegration> defaultAiResult)
            => _defaultAiResult = defaultAiResult;

        public Task<Result<ResolvedIntegration>> ResolveDefaultAiProviderAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_defaultAiResult);

        public Task<Result<ResolvedIntegration>> ResolveProviderAsync(
            Guid organizationId,
            string providerKey,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
