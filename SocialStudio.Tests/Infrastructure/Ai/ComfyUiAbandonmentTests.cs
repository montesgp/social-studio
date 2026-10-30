using System.Net;
using System.Text;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Ai.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Tests.Infrastructure.Ai;

/// <summary>
/// Abandoning a generation must also abandon it on the backend. Before this, giving up on the
/// overall timeout only stopped the HTTP polling: the queued prompt kept rendering on the GPU while
/// the retry queued another one behind it, so every attempt waited out its full budget behind work
/// nobody would collect, and the stacked prompts exhausted video memory
/// (<c>KSampler: could not allocate tensor</c>). Cleanup is targeted at the prompt this call
/// queued — never a blanket interrupt that could kill an unrelated render.
/// </summary>
public sealed class ComfyUiAbandonmentTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public async Task GenerateAsync_AbandonedWhileQueued_DeletesItsOwnPromptFromTheQueue()
    {
        var handler = new AbandonmentHandler(runningPromptId: null);
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(
            handler.Calls,
            call => call.Method == HttpMethod.Post
                && call.Path.EndsWith("/queue", StringComparison.OrdinalIgnoreCase)
                && call.Body.Contains("prompt-123", StringComparison.Ordinal)
                && call.Body.Contains("delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsync_AbandonedWhileItsPromptIsRunning_InterruptsTheBackend()
    {
        var handler = new AbandonmentHandler(runningPromptId: "prompt-123");
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(
            handler.Calls,
            call => call.Method == HttpMethod.Post
                && call.Path.EndsWith("/interrupt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateAsync_AbandonedWhileAnotherPromptIsRunning_DoesNotInterrupt()
    {
        // A blanket /interrupt would kill whatever the backend happens to be rendering. Only the
        // prompt this call queued is ours to cancel.
        var handler = new AbandonmentHandler(runningPromptId: "someone-elses-prompt");
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain(
            handler.Calls,
            call => call.Path.EndsWith("/interrupt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateAsync_AbandonedBeforeQueueing_DoesNotTouchTheQueue()
    {
        // The overall budget elapsed inside POST /prompt, so no prompt id was ever acknowledged:
        // there is nothing of ours to delete, and nothing that could justify an interrupt.
        var handler = new AbandonmentHandler(runningPromptId: null, stallOn: "/prompt");
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain(
            handler.Calls,
            call => call.Path.EndsWith("/interrupt", StringComparison.OrdinalIgnoreCase)
                || (call.Method == HttpMethod.Post && call.Path.EndsWith("/queue", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GenerateAsync_AbandonedAndCleanupFails_StillReportsProviderTimeout()
    {
        // Cleanup is hygiene on a path that has already failed. A backend that rejects /queue or
        // /interrupt must not change the error the caller sees.
        var handler = new AbandonmentHandler(runningPromptId: "prompt-123", cleanupStatusCode: HttpStatusCode.InternalServerError);
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ProviderTimeout.Code, result.Error.Code);
    }

    [Fact]
    public async Task GenerateAsync_AbandonedByTheCallerToken_StillCleansUp()
    {
        // The job-level token — not the provider's own budget — is what cancels here. The queued
        // prompt is just as abandoned either way.
        var handler = new AbandonmentHandler(runningPromptId: "prompt-123");
        var client = BuildClient(handler, timeoutMs: "180000");
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var result = await client.GenerateAsync(BuildRequest(), cts.Token);

        Assert.True(result.IsFailure);
        Assert.Contains(
            handler.Calls,
            call => call.Method == HttpMethod.Post
                && call.Path.EndsWith("/interrupt", StringComparison.OrdinalIgnoreCase));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ComfyUiImageModelClient BuildClient(
        HttpMessageHandler handler,
        string timeoutMs = "150",
        string pollIntervalMs = "1")
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty,
            "comfyui",
            IntegrationConnectionMode.SystemDefault,
            new Dictionary<string, string>
            {
                ["base_url"] = "http://127.0.0.1:8188",
                ["timeout_ms"] = timeoutMs,
                ["poll_interval_ms"] = pollIntervalMs,
            },
            SelectedModel: "sd15-checkpoint.safetensors");

        return new ComfyUiImageModelClient(
            credential,
            new StaticHttpClientFactory(handler),
            new EmptyStorage(),
            NullLogger<ComfyUiImageModelClient>.Instance);
    }

    private static ImageGenerationRequest BuildRequest() => new(
        OrganizationId: OrgId,
        CapabilityKey: "ai-image",
        PromptVersionKey: "image-gen/v1",
        Prompt: "a vivid botanical background",
        Width: 512,
        Height: 512,
        PreferredProviderKey: "comfyui",
        PreferredModel: null);

    // -----------------------------------------------------------------------
    // Test doubles
    // -----------------------------------------------------------------------

    /// <summary>
    /// Scripts upload + queue, then stalls forever on the stalling endpoint so the overall budget
    /// elapses with a prompt outstanding. Records every call so the abandonment cleanup is
    /// observable.
    /// </summary>
    private sealed class AbandonmentHandler : HttpMessageHandler
    {
        private readonly string? _runningPromptId;
        private readonly string _stallOn;
        private readonly HttpStatusCode _cleanupStatusCode;
        private readonly Lock _gate = new();
        private readonly List<Call> _calls = [];

        public AbandonmentHandler(
            string? runningPromptId,
            string stallOn = "/history/",
            HttpStatusCode cleanupStatusCode = HttpStatusCode.OK)
        {
            _runningPromptId = runningPromptId;
            _stallOn = stallOn;
            _cleanupStatusCode = cleanupStatusCode;
        }

        public IReadOnlyList<Call> Calls
        {
            get
            {
                lock (_gate)
                {
                    return [.. _calls];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(CancellationToken.None);

            lock (_gate)
            {
                _calls.Add(new Call(request.Method, path, body));
            }

            if (path.Contains(_stallOn, StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (path.EndsWith("/interrupt", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(_cleanupStatusCode);
            }

            if (path.EndsWith("/queue", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Method == HttpMethod.Post)
                {
                    return new HttpResponseMessage(_cleanupStatusCode);
                }

                var running = _runningPromptId is null
                    ? "[]"
                    : $$"""[[0,"{{_runningPromptId}}",{},{},[]]]""";

                return JsonResponse($$"""{"queue_running":{{running}},"queue_pending":[]}""");
            }

            if (path.Contains("/upload/image", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{"name":"uploaded_ref.png","subfolder":"","type":"input"}""");
            }

            if (path.Contains("/prompt", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{"prompt_id":"prompt-123"}""");
            }

            if (path.Contains("/free", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    internal readonly record struct Call(HttpMethod Method, string Path, string Body);

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StaticHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class EmptyStorage : IGeneratedAssetStorage
    {
        public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
            => Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
        public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
            => Task.FromResult(new StoredAssetRef("path", content.Mime, content.Bytes.Length, "hash"));
        public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
        public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
    }
}
