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
/// WU-4 RED tests (task 4.1): reference-image byte resolution from
/// <see cref="IGeneratedAssetStorage"/>, capped to <c>ComfyUiSettings.MaxReferenceImages</c>;
/// failed <c>OpenAsync</c> handles are logged and skipped; a zero-resolve set falls through to
/// the descriptor-only (no-upload) path. Refs: CUI-01-a, CUI-01-b, CUI-06-a; design §0 OQ-5,
/// §3.4 steps 0-1.
/// </summary>
public sealed class ComfyUiReferenceResolutionTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly byte[] FixtureImageBytes = [0x89, 0x50, 0x4E, 0x47];

    [Fact]
    public async Task GenerateAsync_MultipleResolvableRefsWithinCap_UploadsEachRef()
    {
        var storage = new FakeStorage(new Dictionary<string, byte[]>
        {
            ["ref1.jpg"] = [1, 2, 3],
            ["ref2.jpg"] = [4, 5, 6],
        });
        var handler = new TrackingHandler(FixtureImageBytes);
        var client = BuildClient(handler, storage, maxReferenceImages: "2");

        var request = BuildRequest() with
        {
            ReferenceImages = new List<ImageInitReference>
            {
                new(Bytes: null, Url: "ref1.jpg", Mime: "image/jpeg"),
                new(Bytes: null, Url: "ref2.jpg", Mime: "image/jpeg"),
            },
        };

        var result = await client.GenerateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.UploadCallCount);
        Assert.Contains("ref-upload-0", handler.PromptRequestBody);
    }

    [Fact]
    public async Task GenerateAsync_ThreeRefsWithCapOne_OnlyFirstRefUploaded()
    {
        var storage = new FakeStorage(new Dictionary<string, byte[]>
        {
            ["ref1.jpg"] = [1],
            ["ref2.jpg"] = [2],
            ["ref3.jpg"] = [3],
        });
        var handler = new TrackingHandler(FixtureImageBytes);
        var client = BuildClient(handler, storage, maxReferenceImages: "1");

        var request = BuildRequest() with
        {
            ReferenceImages = new List<ImageInitReference>
            {
                new(Bytes: null, Url: "ref1.jpg", Mime: "image/jpeg"),
                new(Bytes: null, Url: "ref2.jpg", Mime: "image/jpeg"),
                new(Bytes: null, Url: "ref3.jpg", Mime: "image/jpeg"),
            },
        };

        var result = await client.GenerateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, handler.UploadCallCount);
        Assert.Equal(1, storage.OpenAsyncCallCount);
    }

    [Fact]
    public async Task GenerateAsync_OneRefOpenAsyncThrows_SkippedAndRemainingProceed()
    {
        var storage = new ThrowingThenSucceedingStorage(throwOnUrl: "ref1.jpg", succeedBytes: [9, 9, 9]);
        var handler = new TrackingHandler(FixtureImageBytes);
        var client = BuildClient(handler, storage, maxReferenceImages: "2");

        var request = BuildRequest() with
        {
            ReferenceImages = new List<ImageInitReference>
            {
                new(Bytes: null, Url: "ref1.jpg", Mime: "image/jpeg"),
                new(Bytes: null, Url: "ref2.jpg", Mime: "image/jpeg"),
            },
        };

        var result = await client.GenerateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, handler.UploadCallCount);
    }

    [Fact]
    public async Task GenerateAsync_AllRefsFail_ZeroResolved_DescriptorOnlyPathNoUpload()
    {
        var storage = new FakeStorage(new Dictionary<string, byte[]>());
        var handler = new TrackingHandler(FixtureImageBytes);
        var client = BuildClient(handler, storage, maxReferenceImages: "2");

        var request = BuildRequest() with
        {
            ReferenceImages = new List<ImageInitReference>
            {
                new(Bytes: null, Url: "missing1.jpg", Mime: "image/jpeg"),
                new(Bytes: null, Url: "missing2.jpg", Mime: "image/jpeg"),
            },
        };

        var result = await client.GenerateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, handler.UploadCallCount);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ComfyUiImageModelClient BuildClient(
        HttpMessageHandler handler,
        IGeneratedAssetStorage storage,
        string maxReferenceImages)
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty,
            "comfyui",
            IntegrationConnectionMode.SystemDefault,
            new Dictionary<string, string>
            {
                ["base_url"] = "http://127.0.0.1:8188",
                ["poll_interval_ms"] = "1",
                ["max_reference_images"] = maxReferenceImages,
            },
            SelectedModel: "sd15-checkpoint.safetensors");

        return new ComfyUiImageModelClient(
            credential,
            new StaticHttpClientFactory(handler),
            storage,
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

    /// <summary>Scripts the full happy path and tracks /upload/image call count + last /prompt body.</summary>
    private sealed class TrackingHandler : HttpMessageHandler
    {
        private readonly byte[] _fixtureBytes;
        private int _historyCallCount;
        private int _uploadCallCount;

        public int UploadCallCount => _uploadCallCount;
        public string PromptRequestBody { get; private set; } = string.Empty;

        public TrackingHandler(byte[] fixtureBytes) => _fixtureBytes = fixtureBytes;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.Contains("/upload/image", StringComparison.OrdinalIgnoreCase))
            {
                var name = $"ref-upload-{_uploadCallCount}";
                _uploadCallCount++;
                return JsonResponse($$"""{"name":"{{name}}","subfolder":"","type":"input"}""");
            }

            if (path.Contains("/history/", StringComparison.OrdinalIgnoreCase))
            {
                _historyCallCount++;
                if (_historyCallCount < 2)
                {
                    return JsonResponse("{}");
                }

                return JsonResponse(
                    """{"prompt-123":{"outputs":{"9":{"images":[{"filename":"out.png","subfolder":"","type":"output"}]}}}}""");
            }

            if (path.Contains("/prompt", StringComparison.OrdinalIgnoreCase))
            {
                PromptRequestBody = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                return JsonResponse("""{"prompt_id":"prompt-123"}""");
            }

            if (path.Contains("/view", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_fixtureBytes),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StaticHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class FakeStorage : IGeneratedAssetStorage
    {
        private readonly IReadOnlyDictionary<string, byte[]> _pathToBytes;
        private int _openCallCount;

        public int OpenAsyncCallCount => _openCallCount;

        public FakeStorage(IReadOnlyDictionary<string, byte[]> pathToBytes) => _pathToBytes = pathToBytes;

        public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
        {
            Interlocked.Increment(ref _openCallCount);
            if (_pathToBytes.TryGetValue(storagePath, out var bytes))
            {
                return Task.FromResult(Result.Success(new StoredAssetStream(new MemoryStream(bytes), "image/jpeg", bytes.Length)));
            }

            return Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
        }

        public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
            => Task.FromResult(new StoredAssetRef("path", content.Mime, content.Bytes.Length, "hash"));
        public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
        public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Throws synchronously for one specific URL; resolves bytes for any other URL.</summary>
    private sealed class ThrowingThenSucceedingStorage : IGeneratedAssetStorage
    {
        private readonly string _throwOnUrl;
        private readonly byte[] _succeedBytes;

        public ThrowingThenSucceedingStorage(string throwOnUrl, byte[] succeedBytes)
        {
            _throwOnUrl = throwOnUrl;
            _succeedBytes = succeedBytes;
        }

        public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
        {
            if (storagePath == _throwOnUrl)
            {
                throw new IOException("Simulated storage failure.");
            }

            return Task.FromResult(Result.Success(new StoredAssetStream(new MemoryStream(_succeedBytes), "image/jpeg", _succeedBytes.Length)));
        }

        public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
            => Task.FromResult(new StoredAssetRef("path", content.Mime, content.Bytes.Length, "hash"));
        public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
        public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
    }
}
