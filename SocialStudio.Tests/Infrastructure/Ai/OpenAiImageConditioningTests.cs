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
/// WU-4 RED tests (task 4.5 — byte resolution + edits endpoint):
/// <c>OpenAiImageModelClient</c> resolves bytes from <see cref="IGeneratedAssetStorage"/> for
/// <see cref="ImageInitReference"/> Url handles and routes to the OpenAI image-edits endpoint
/// (<c>/v1/images/edits</c>) as <c>multipart/form-data</c>.
///
/// <para>When no reference images are present (or all fail to resolve), the adapter falls
/// back to the standard <c>/v1/images/generations</c> (JSON) path unchanged.</para>
///
/// <para>References beyond <c>MaxConditioningImages</c> are capped; storage is only called for
/// the capped set. References that fail to resolve are skipped (warning logged); generation
/// proceeds with the successfully resolved images. If all fail, falls back to generations.</para>
///
/// Refs: CAC-IMG-01; design §3.4, §6 (boundary table), line 215; tasks 4.5.
/// </summary>
public sealed class OpenAiImageConditioningTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    // -----------------------------------------------------------------------
    // CAC-IMG-01: Url handles present → calls OpenAsync per handle
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_WithUrlHandles_CallsStorageOpenAsyncForEachRef()
    {
        var refBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var storage = new ConditioningFakeStorage(new Dictionary<string, byte[]>
        {
            ["org/ref1.jpg"] = refBytes,
            ["org/ref2.jpg"] = refBytes,
        });
        var handler = new ConditioningCapturingHandler();
        var client = BuildClient(handler, storage);

        var request = BuildRequest() with
        {
            ReferenceImages = new List<ImageInitReference>
            {
                new(Bytes: null, Url: "org/ref1.jpg", Mime: "image/jpeg"),
                new(Bytes: null, Url: "org/ref2.jpg", Mime: "image/jpeg"),
            }
        };

        await client.GenerateAsync(request, CancellationToken.None);

        Assert.Equal(2, storage.OpenAsyncCallCount);
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-01: resolved bytes → routes to /v1/images/edits
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_WithUrlHandles_PostsToEditsEndpoint()
    {
        var refBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var storage = new ConditioningFakeStorage(new Dictionary<string, byte[]>
        {
            ["org/ref1.jpg"] = refBytes
        });
        var handler = new ConditioningCapturingHandler();
        var client = BuildClient(handler, storage);

        var request = BuildRequest() with
        {
            ReferenceImages = new List<ImageInitReference>
            {
                new(Bytes: null, Url: "org/ref1.jpg", Mime: "image/jpeg"),
            }
        };

        await client.GenerateAsync(request, CancellationToken.None);

        Assert.NotNull(handler.CapturedUri);
        Assert.Contains("/images/edits", handler.CapturedUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-01: edits request is multipart/form-data and contains image bytes
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_WithUrlHandles_RequestIsMultipartContainingResolvedBytes()
    {
        var refBytes = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }; // distinctive pattern
        var storage = new ConditioningFakeStorage(new Dictionary<string, byte[]>
        {
            ["org/ref1.jpg"] = refBytes
        });
        var handler = new ConditioningCapturingHandler();
        var client = BuildClient(handler, storage);

        var request = BuildRequest() with
        {
            ReferenceImages = new List<ImageInitReference>
            {
                new(Bytes: null, Url: "org/ref1.jpg", Mime: "image/jpeg"),
            }
        };

        await client.GenerateAsync(request, CancellationToken.None);

        // Content-Type must be multipart/form-data
        var contentType = handler.CapturedContentType;
        Assert.NotNull(contentType);
        Assert.Contains("multipart/form-data", contentType, StringComparison.OrdinalIgnoreCase);

        // The raw multipart body must contain the resolved image bytes
        var body = handler.CapturedBodyBytes;
        Assert.NotNull(body);
        Assert.True(ContainsBytes(body!, refBytes),
            "Expected resolved image bytes to appear verbatim in the multipart body.");
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-01: no reference images → falls back to /v1/images/generations
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_WithNoReferenceImages_PostsToGenerationsEndpoint()
    {
        var storage = new ConditioningFakeStorage(new Dictionary<string, byte[]>());
        var handler = new ConditioningCapturingHandler();
        var client = BuildClient(handler, storage);

        // No ReferenceImages
        var request = BuildRequest();

        await client.GenerateAsync(request, CancellationToken.None);

        Assert.NotNull(handler.CapturedUri);
        Assert.Contains("/images/generations", handler.CapturedUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-01: no reference images → storage NOT called
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_WithNoReferenceImages_DoesNotCallStorage()
    {
        var storage = new ConditioningFakeStorage(new Dictionary<string, byte[]>());
        var handler = new ConditioningCapturingHandler();
        var client = BuildClient(handler, storage);

        await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(0, storage.OpenAsyncCallCount);
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-01: more refs than MaxConditioningImages → storage capped to Max
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_MoreRefsThanMax_StorageCalledOnlyMaxConditioningImagesTimes()
    {
        var refBytes = new byte[] { 0x01, 0x02, 0x03 };
        var storage = new ConditioningFakeStorage(new Dictionary<string, byte[]>
        {
            ["ref1.jpg"] = refBytes,
            ["ref2.jpg"] = refBytes,
            ["ref3.jpg"] = refBytes,
            ["ref4.jpg"] = refBytes,
            ["ref5.jpg"] = refBytes,
        });
        var handler = new ConditioningCapturingHandler();
        var client = BuildClient(handler, storage);

        // 5 refs but MaxConditioningImages = 3
        var request = BuildRequest() with
        {
            ReferenceImages = new List<ImageInitReference>
            {
                new(Bytes: null, Url: "ref1.jpg", Mime: "image/jpeg"),
                new(Bytes: null, Url: "ref2.jpg", Mime: "image/jpeg"),
                new(Bytes: null, Url: "ref3.jpg", Mime: "image/jpeg"),
                new(Bytes: null, Url: "ref4.jpg", Mime: "image/jpeg"),
                new(Bytes: null, Url: "ref5.jpg", Mime: "image/jpeg"),
            }
        };

        await client.GenerateAsync(request, CancellationToken.None);

        // OpenAiImageModelClient.Capabilities.MaxConditioningImages == 3
        Assert.Equal(3, storage.OpenAsyncCallCount);
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-01: one ref fails to resolve → skipped; generation still proceeds
    // with remaining resolved refs (edits endpoint, no exception thrown)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_OneOpenAsyncFails_SkippedAndGenerationProceeds()
    {
        var refBytes = new byte[] { 0x01, 0x02, 0x03 };
        // Only ref1 resolves; ref2 is unknown (returns failure)
        var storage = new ConditioningFakeStorage(new Dictionary<string, byte[]>
        {
            ["ref1.jpg"] = refBytes,
            // "ref2.jpg" intentionally absent → failure
        });
        var handler = new ConditioningCapturingHandler();
        var client = BuildClient(handler, storage);

        var request = BuildRequest() with
        {
            ReferenceImages = new List<ImageInitReference>
            {
                new(Bytes: null, Url: "ref1.jpg", Mime: "image/jpeg"),
                new(Bytes: null, Url: "ref2.jpg", Mime: "image/jpeg"), // will fail
            }
        };

        // Must not throw — ref2 failure is logged + skipped
        var result = await client.GenerateAsync(request, CancellationToken.None);

        // Generation proceeds with ref1 only → edits endpoint
        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.CapturedUri);
        Assert.Contains("/images/edits", handler.CapturedUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-01: all refs fail → falls back to generations (no exception)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_AllOpenAsyncFail_FallsBackToGenerations()
    {
        // No refs resolve (empty dictionary → all OpenAsync calls return failure)
        var storage = new ConditioningFakeStorage(new Dictionary<string, byte[]>());
        var handler = new ConditioningCapturingHandler();
        var client = BuildClient(handler, storage);

        var request = BuildRequest() with
        {
            ReferenceImages = new List<ImageInitReference>
            {
                new(Bytes: null, Url: "missing1.jpg", Mime: "image/jpeg"),
                new(Bytes: null, Url: "missing2.jpg", Mime: "image/jpeg"),
            }
        };

        var result = await client.GenerateAsync(request, CancellationToken.None);

        // Fallback: all failed → generations path
        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.CapturedUri);
        Assert.Contains("/images/generations", handler.CapturedUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-02 + CAC-IMG-01: BackgroundOnly directive is preserved on the edits path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_WithUrlHandlesAndBackgroundOnly_DirectiveIncludedInPromptField()
    {
        const string directive = "generate ONLY the background; never render text, logos, @handles, or signatures";
        var refBytes = new byte[] { 0x01, 0x02 };
        var storage = new ConditioningFakeStorage(new Dictionary<string, byte[]>
        {
            ["ref1.jpg"] = refBytes
        });
        var handler = new ConditioningCapturingHandler();
        var client = BuildClient(handler, storage);

        var request = BuildRequest() with
        {
            Prompt = "botanical textures",
            BackgroundOnly = true,
            ReferenceImages = new List<ImageInitReference>
            {
                new(Bytes: null, Url: "ref1.jpg", Mime: "image/jpeg"),
            }
        };

        await client.GenerateAsync(request, CancellationToken.None);

        // The multipart body must contain the directive in the prompt part
        var bodyBytes = handler.CapturedBodyBytes;
        Assert.NotNull(bodyBytes);
        var bodyText = Encoding.UTF8.GetString(bodyBytes!);
        Assert.Contains(directive, bodyText, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static OpenAiImageModelClient BuildClient(
        HttpMessageHandler handler,
        IGeneratedAssetStorage storage)
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty, "openai", IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string> { ["api_key"] = "test-key-cond" },
            SelectedModel: "gpt-image-1");

        return new OpenAiImageModelClient(
            credential,
            new ConditioningHttpClientFactory(handler),
            storage,
            NullLogger<OpenAiImageModelClient>.Instance);
    }

    private static ImageGenerationRequest BuildRequest() => new(
        OrganizationId: OrgId,
        CapabilityKey: "ai-image",
        PromptVersionKey: "image-gen/v1",
        Prompt: "a vivid botanical background",
        Width: 1024,
        Height: 1024,
        PreferredProviderKey: "openai",
        PreferredModel: "gpt-image-1");

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0) return true;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle.AsSpan()))
                return true;
        }
        return false;
    }
}

// -----------------------------------------------------------------------
// Test doubles
// -----------------------------------------------------------------------

/// <summary>
/// Captures URI, content-type and raw body bytes from the outgoing HTTP request.
/// Returns a synthetic OpenAI success response so generation completes normally.
/// </summary>
file sealed class ConditioningCapturingHandler : HttpMessageHandler
{
    private Uri? _capturedUri;
    private string? _capturedContentType;
    private byte[]? _capturedBodyBytes;

    public Uri? CapturedUri => _capturedUri;
    public string? CapturedContentType => _capturedContentType;
    public byte[]? CapturedBodyBytes => _capturedBodyBytes;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _capturedUri = request.RequestUri;
        _capturedContentType = request.Content?.Headers.ContentType?.ToString();

        if (request.Content is not null)
        {
            _capturedBodyBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        // Synthetic OpenAI success response (b64_json format)
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A };
        var b64 = Convert.ToBase64String(imageBytes);
        var responseBody = $$"""{"created":1700000000,"data":[{"b64_json":"{{b64}}"}]}""";

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };
    }
}

file sealed class ConditioningHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public ConditioningHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

/// <summary>
/// Fake <see cref="IGeneratedAssetStorage"/> that returns pre-configured bytes for known paths.
/// Tracks how many times <see cref="OpenAsync"/> was called.
/// </summary>
file sealed class ConditioningFakeStorage : IGeneratedAssetStorage
{
    private readonly IReadOnlyDictionary<string, byte[]> _pathToBytes;
    private int _openCallCount;

    public int OpenAsyncCallCount => _openCallCount;

    public ConditioningFakeStorage(IReadOnlyDictionary<string, byte[]> pathToBytes)
        => _pathToBytes = pathToBytes;

    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
    {
        Interlocked.Increment(ref _openCallCount);
        if (_pathToBytes.TryGetValue(storagePath, out var bytes))
        {
            var stream = new MemoryStream(bytes);
            return Task.FromResult(Result.Success(new StoredAssetStream(stream, "image/jpeg", bytes.Length)));
        }

        return Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
    }

    // Unused members (satisfy interface)
    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
        => Task.FromResult(new StoredAssetRef("path", content.Mime, content.Bytes.Length, "hash"));
    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}
