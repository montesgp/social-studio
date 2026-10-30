using System.Net;
using System.Text;
using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Ai.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Tests.Infrastructure.Ai;

/// <summary>
/// WU-4 RED tests (task 4.7): BackgroundOnly directive injection in the OpenAiImageModelClient.
/// When <see cref="ImageGenerationRequest.BackgroundOnly"/> is true the request sent to the
/// provider's API includes "generate ONLY the background; never render text, logos, @handles,
/// or signatures" in the prompt (CAC-IMG-02-a).
/// Non-BackgroundOnly requests do NOT receive the directive (CAC-IMG-02-b).
/// Refs: CAC-IMG-02; design §3.4; tasks 4.7.
/// </summary>
public sealed class BackgroundOnlyDirectiveTests
{
    private const string BackgroundOnlyDirective =
        "generate ONLY the background; never render text, logos, @handles, or signatures";

    private static readonly Guid OrgId = Guid.NewGuid();

    // -----------------------------------------------------------------------
    // CAC-IMG-02-a: BackgroundOnly=true → prompt sent to provider contains the directive
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_BackgroundOnlyTrue_PromptContainsSystemDirective()
    {
        var captureHandler = new CapturingRequestHandler();
        var client = BuildClient(captureHandler);

        var request = BuildRequest() with
        {
            Prompt = "a rich botanical texture",
            BackgroundOnly = true
        };

        await client.GenerateAsync(request, CancellationToken.None);

        Assert.True(captureHandler.WasCalled, "Expected at least one HTTP call.");
        var sentBody = captureHandler.CapturedRequestBody;
        Assert.NotNull(sentBody);

        // The prompt in the body must include the directive
        Assert.Contains(BackgroundOnlyDirective, sentBody, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-02-b: BackgroundOnly=false (default) → prompt does NOT contain the directive
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_BackgroundOnlyFalse_PromptDoesNotContainDirective()
    {
        var captureHandler = new CapturingRequestHandler();
        var client = BuildClient(captureHandler);

        var request = BuildRequest() with
        {
            Prompt = "a vivid social media background",
            BackgroundOnly = false
        };

        await client.GenerateAsync(request, CancellationToken.None);

        Assert.True(captureHandler.WasCalled, "Expected at least one HTTP call.");
        var sentBody = captureHandler.CapturedRequestBody;
        Assert.NotNull(sentBody);

        Assert.DoesNotContain(BackgroundOnlyDirective, sentBody, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-02-a: directive is prepended/included in the prompt, not as a suffix
    // (ordering doesn't block render; we just check it appears)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_BackgroundOnlyTrue_OriginalPromptIsAlsoIncluded()
    {
        const string originalPrompt = "botanical textures, warm earthy tones, organic shapes";
        var captureHandler = new CapturingRequestHandler();
        var client = BuildClient(captureHandler);

        var request = BuildRequest() with
        {
            Prompt = originalPrompt,
            BackgroundOnly = true
        };

        await client.GenerateAsync(request, CancellationToken.None);

        var sentBody = captureHandler.CapturedRequestBody;
        Assert.NotNull(sentBody);

        // Both directive AND original prompt must be in the sent body
        Assert.Contains(BackgroundOnlyDirective, sentBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(originalPrompt, sentBody, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static OpenAiImageModelClient BuildClient(HttpMessageHandler handler)
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty, "openai", IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string> { ["api_key"] = "test-key-456" },
            SelectedModel: "dall-e-3");

        return new OpenAiImageModelClient(
            credential,
            new DirectiveHttpClientFactory(handler),
            new NullDirectiveStorage(),
            NullLogger<OpenAiImageModelClient>.Instance);
    }

    private static ImageGenerationRequest BuildRequest() => new(
        OrganizationId: OrgId,
        CapabilityKey: "ai-image",
        PromptVersionKey: "image-gen/v1",
        Prompt: "base prompt",
        Width: 1024,
        Height: 1024,
        PreferredProviderKey: "openai",
        PreferredModel: "dall-e-3");
}

// -----------------------------------------------------------------------
// Test doubles
// -----------------------------------------------------------------------

file sealed class CapturingRequestHandler : HttpMessageHandler
{
    private string? _capturedBody;
    private bool _wasCalled;

    public bool WasCalled => _wasCalled;
    public string? CapturedRequestBody => _capturedBody;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _wasCalled = true;
        _capturedBody = request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken)
            : null;

        // Return a valid OpenAI response so GenerateAsync doesn't short-circuit before building the body
        var imageBytes = new byte[] { 137, 80, 78, 71 }; // PNG magic
        var b64 = Convert.ToBase64String(imageBytes);
        var responseBody = $$"""{"created":1700000000,"data":[{"b64_json":"{{b64}}"}]}""";

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };
    }
}

file sealed class DirectiveHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public DirectiveHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

/// <summary>No-op storage — these tests only exercise the directive path (no conditioning).</summary>
file sealed class NullDirectiveStorage : IGeneratedAssetStorage
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
