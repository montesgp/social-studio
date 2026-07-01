using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai.Providers;
using Incoders.Hive.Infrastructure.Integrations;
using Microsoft.Extensions.DependencyInjection;

namespace Incoders.Hive.Tests.Infrastructure.Integrations;

/// <summary>
/// WU-6 RED tests (task 6.1): <c>ExternalProviderClientFactory</c> resolves the <c>comfyui</c>
/// provider key to the ComfyUI adapter/catalog types, and non-comfyui keys are unaffected.
/// Refs: CUI-INT-04-a, CUI-INT-04-b; design §2.2, §13.
/// </summary>
public sealed class ComfyUiProviderFactoryTests
{
    [Fact]
    public void Create_ImageModelClient_ForComfyUiProvider_ReturnsComfyUiImageModelClient()
    {
        var factory = BuildFactory();

        var client = factory.Create<IImageModelClient>(BuildComfyUiCredential());

        Assert.IsType<ComfyUiImageModelClient>(client);
    }

    [Fact]
    public void Create_ModelCatalogClient_ForComfyUiProvider_ReturnsComfyUiModelCatalogClient()
    {
        var factory = BuildFactory();

        var client = factory.Create<IAiModelCatalogClient>(BuildComfyUiCredential());

        Assert.IsType<ComfyUiModelCatalogClient>(client);
    }

    [Fact]
    public void Create_ImageModelClient_ForOpenAiProvider_DoesNotReturnComfyUiImageModelClient()
    {
        var factory = BuildFactory();

        var client = factory.Create<IImageModelClient>(BuildOpenAiCredential());

        Assert.IsNotType<ComfyUiImageModelClient>(client);
        Assert.IsType<OpenAiImageModelClient>(client);
    }

    private static ExternalProviderClientFactory BuildFactory()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton<IGeneratedAssetStorage, NoopGeneratedAssetStorage>();
        return new ExternalProviderClientFactory(services.BuildServiceProvider());
    }

    private static ResolvedProviderCredential BuildComfyUiCredential() =>
        new(Guid.Empty, "comfyui", IntegrationConnectionMode.SystemDefault,
            new Dictionary<string, string> { ["base_url"] = "http://127.0.0.1:8188" }, SelectedModel: null);

    private static ResolvedProviderCredential BuildOpenAiCredential() =>
        new(Guid.Empty, "openai", IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string> { ["api_key"] = "sk-test" }, SelectedModel: null);

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
