using System.Net;
using System.Text;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai.Providers;
using Incoders.Hive.Infrastructure.Process;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Infrastructure.Ai;

/// <summary>
/// WU-4 RED tests (task 4.4): IImageModelClient.Capabilities is a property, not a provider call.
/// Supporting adapter (OpenAiImageModelClient) returns SupportsImageConditioning=true + positive
/// MaxConditioningImages. Non-supporting adapter (ClaudePromptCliImageModelClient) returns false.
/// Refs: CAC-IMG-01; design §3.4; tasks 4.4.
/// </summary>
public sealed class ImageModelClientCapabilityTests
{
    // -----------------------------------------------------------------------
    // 4.4a: OpenAiImageModelClient.Capabilities returns SupportsImageConditioning=true
    // -----------------------------------------------------------------------

    [Fact]
    public void OpenAiImageModelClient_Capabilities_ReturnsSupportsImageConditioningTrue()
    {
        var handler = new CapabilityNeverCalledHandler();
        var client = BuildOpenAiClient(handler);

        var caps = client.Capabilities;

        Assert.True(caps.SupportsImageConditioning);
    }

    // -----------------------------------------------------------------------
    // 4.4b: OpenAiImageModelClient.Capabilities returns positive MaxConditioningImages
    // -----------------------------------------------------------------------

    [Fact]
    public void OpenAiImageModelClient_Capabilities_ReturnsPositiveMaxConditioningImages()
    {
        var handler = new CapabilityNeverCalledHandler();
        var client = BuildOpenAiClient(handler);

        var caps = client.Capabilities;

        Assert.True(caps.MaxConditioningImages > 0,
            $"MaxConditioningImages must be positive, got {caps.MaxConditioningImages}");
    }

    // -----------------------------------------------------------------------
    // 4.4c: OpenAiImageModelClient.Capabilities is a property — does NOT call the provider
    // -----------------------------------------------------------------------

    [Fact]
    public void OpenAiImageModelClient_Capabilities_DoesNotCallProvider()
    {
        var handler = new CapabilityNeverCalledHandler();
        var client = BuildOpenAiClient(handler);

        _ = client.Capabilities;

        Assert.Equal(0, handler.CallCount);
    }

    // -----------------------------------------------------------------------
    // 4.4d: ClaudePromptCliImageModelClient.Capabilities returns SupportsImageConditioning=false
    // -----------------------------------------------------------------------

    [Fact]
    public void ClaudePromptCliImageModelClient_Capabilities_ReturnsSupportsImageConditioningFalse()
    {
        var processRunner = new CapabilityNeverCalledProcessRunner();
        var client = BuildClaudeClient(processRunner);

        var caps = client.Capabilities;

        Assert.False(caps.SupportsImageConditioning);
    }

    // -----------------------------------------------------------------------
    // 4.4e: ClaudePromptCliImageModelClient.Capabilities is a property — does NOT call the process
    // -----------------------------------------------------------------------

    [Fact]
    public void ClaudePromptCliImageModelClient_Capabilities_DoesNotCallProcess()
    {
        var processRunner = new CapabilityNeverCalledProcessRunner();
        var client = BuildClaudeClient(processRunner);

        _ = client.Capabilities;

        Assert.Equal(0, processRunner.CallCount);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static OpenAiImageModelClient BuildOpenAiClient(HttpMessageHandler handler)
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty, "openai", IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string> { ["api_key"] = "test-key" },
            SelectedModel: "dall-e-3");

        return new OpenAiImageModelClient(
            credential,
            new CapabilityHttpClientFactory(handler),
            new NullCapabilityStorage(),
            NullLogger<OpenAiImageModelClient>.Instance);
    }

    private static ClaudePromptCliImageModelClient BuildClaudeClient(IProcessRunner processRunner)
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty, "claude-p", IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string>
            {
                ["executable_path"] = "claude",
                ["working_directory"] = ".",
                ["allowed_output_directory"] = "."
            },
            SelectedModel: "claude-sonnet-4-5");

        return new ClaudePromptCliImageModelClient(
            credential,
            processRunner,
            NullLogger<ClaudePromptCliImageModelClient>.Instance);
    }
}

// -----------------------------------------------------------------------
// Test doubles
// -----------------------------------------------------------------------

file sealed class CapabilityNeverCalledHandler : HttpMessageHandler
{
    private int _callCount;
    public int CallCount => _callCount;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
    }
}

file sealed class CapabilityHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public CapabilityHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

/// <summary>No-op storage — capability tests never call GenerateAsync, so storage is unused.</summary>
file sealed class NullCapabilityStorage : IGeneratedAssetStorage
{
    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
        => Task.FromResult(new StoredAssetRef("path", content.Mime, content.Bytes.Length, "hash"));
    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
        => Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}

file sealed class CapabilityNeverCalledProcessRunner : IProcessRunner
{
    private int _callCount;
    public int CallCount => _callCount;

    public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(new ProcessRunResult(
            ExitCode: 0,
            StandardOutput: string.Empty,
            StandardError: string.Empty,
            TimedOut: false,
            OutputLimitExceeded: false));
    }
}
