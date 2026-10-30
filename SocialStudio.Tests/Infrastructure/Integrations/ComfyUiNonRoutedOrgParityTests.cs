using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Ai.Providers;
using SocialStudio.Infrastructure.Integrations;
using SocialStudio.Infrastructure.Process;
using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Tests.Infrastructure.Integrations;

/// <summary>
/// WU-6 RED tests (task 6.10): adding the <c>"comfyui"</c> factory arm is purely additive — orgs
/// routed to <c>openai</c>/<c>claude-p</c>/CSS-only, and every other existing provider key, resolve
/// exactly as before. Refs: CUI-IMG-02-a, CUI-INT-05-a.
/// </summary>
public sealed class ComfyUiNonRoutedOrgParityTests
{
    [Fact]
    public void OrgRoutedToOpenAi_FactoryReturnsOpenAiImageModelClient_NeverComfyUi()
    {
        var factory = BuildFactory();

        var client = factory.Create<IImageModelClient>(BuildCredential("openai", new Dictionary<string, string> { ["api_key"] = "sk-test" }));

        Assert.IsType<OpenAiImageModelClient>(client);
        Assert.IsNotType<ComfyUiImageModelClient>(client);
    }

    [Fact]
    public void OrgRoutedToClaudeP_FactoryReturnsClaudePromptCliImageModelClient_NeverComfyUi()
    {
        var factory = BuildFactory();

        var client = factory.Create<IImageModelClient>(BuildCredential("claude-p", new Dictionary<string, string>()));

        Assert.IsType<ClaudePromptCliImageModelClient>(client);
        Assert.IsNotType<ComfyUiImageModelClient>(client);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("claude-p")]
    [InlineData("claude")]
    [InlineData("anthropic")]
    [InlineData("gemini")]
    public void ExistingAiModelProviderKeys_StillResolveCorrectly_AfterComfyUiArmAdded(string providerKey)
    {
        var factory = BuildFactory();

        var client = factory.Create<IAiModelClient>(BuildCredential(providerKey, new Dictionary<string, string> { ["api_key"] = "key" }));

        Assert.NotNull(client);
    }

    [Fact]
    public void UnknownProviderKey_DoesNotResolveComfyUiClient()
    {
        var factory = BuildFactory();

        var act = () => factory.Create<IImageModelClient>(BuildCredential("some-unrelated-provider", new Dictionary<string, string>()));

        Assert.Throws<InvalidOperationException>(act);
    }

    private static ExternalProviderClientFactory BuildFactory()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton<IProcessRunner, NoopProcessRunner>();
        services.AddSingleton<IGeneratedAssetStorage, NoopGeneratedAssetStorage>();
        return new ExternalProviderClientFactory(services.BuildServiceProvider());
    }

    private static ResolvedProviderCredential BuildCredential(string providerKey, IReadOnlyDictionary<string, string> credentials) =>
        new(Guid.Empty, providerKey, IntegrationConnectionMode.BringYourOwnKey, credentials, SelectedModel: null);

    private sealed class NoopProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessRunResult(0, "{}", "", false));
    }

    private sealed class NoopGeneratedAssetStorage : IGeneratedAssetStorage
    {
        public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct) =>
            Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
        public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct) =>
            Task.FromResult(new StoredAssetRef("path", content.Mime, content.Bytes.Length, "hash"));
        public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
        public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
    }
}
