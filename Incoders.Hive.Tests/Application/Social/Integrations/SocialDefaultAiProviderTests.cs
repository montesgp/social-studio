using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Features.Social.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Tests.Application.Social.Integrations;

public sealed class SocialDefaultAiProviderTests
{
    [Fact]
    public async Task Handler_UsesIntegrationResolverForDefaultAi()
    {
        var organizationId = Guid.NewGuid();
        var resolver = new FakeResolver(
            Result.Success(new ResolvedIntegration(
                organizationId,
                "openai",
                "OpenAI",
                IntegrationProviderCategory.AiModel,
                IntegrationConnectionMode.BringYourOwnKey,
                IntegrationStatus.Connected,
                false,
                true,
                "gpt-4.1-mini",
                new Dictionary<string, string>())));

        var handler = new GetSocialDefaultAiProviderHandler(resolver);

        var result = await handler.Handle(new GetSocialDefaultAiProviderQuery(organizationId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("openai", result.Value.ProviderKey);
        Assert.Equal("gpt-4.1-mini", result.Value.Model);
        Assert.Equal(organizationId, resolver.LastOrganizationId);
    }

    private sealed class FakeResolver : IIntegrationResolver
    {
        private readonly Result<ResolvedIntegration> _result;

        public FakeResolver(Result<ResolvedIntegration> result)
        {
            _result = result;
        }

        public Guid? LastOrganizationId { get; private set; }

        public Task<Result<ResolvedIntegration>> ResolveProviderAsync(
            Guid organizationId,
            string providerKey,
            CancellationToken cancellationToken = default)
        {
            _ = providerKey;
            _ = cancellationToken;
            LastOrganizationId = organizationId;
            return Task.FromResult(_result);
        }

        public Task<Result<ResolvedIntegration>> ResolveDefaultAiProviderAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            LastOrganizationId = organizationId;
            return Task.FromResult(_result);
        }
    }
}