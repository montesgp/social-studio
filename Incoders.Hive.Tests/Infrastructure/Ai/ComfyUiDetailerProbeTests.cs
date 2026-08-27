using System.Net;
using System.Text;
using Incoders.Hive.Infrastructure.Ai.Providers;

namespace Incoders.Hive.Tests.Infrastructure.Ai;

/// <summary>
/// Phase 1 RED/GREEN tests (tasks 1.1-1.5): <c>ComfyUiDetailerProbe</c> tri-state classification,
/// TTL cache, invalidation, and <c>node_errors</c> attribution.
/// Refs: CUI-07, CUI-09; design §A1-A4.
/// </summary>
public sealed class ComfyUiDetailerProbeTests
{
    private const string BaseUrl = "http://127.0.0.1:8188";

    public ComfyUiDetailerProbeTests() => ComfyUiDetailerProbe.ResetCacheForTests();

    // -----------------------------------------------------------------------
    // 1.1 — classification: presence/absence/partial
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_BothClassesPresent_ReturnsSupported()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, ObjectInfoBody(faceDetailer: true, detectorProvider: true));

        var result = await ComfyUiDetailerProbe.ProbeAsync(new HttpClient(handler), BaseUrl, CancellationToken.None);

        Assert.Equal(ComfyUiDetailerSupport.Supported, result);
    }

    [Fact]
    public async Task ProbeAsync_NeitherClassPresent_ReturnsNotSupported()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, ObjectInfoBody(faceDetailer: false, detectorProvider: false));

        var result = await ComfyUiDetailerProbe.ProbeAsync(new HttpClient(handler), BaseUrl, CancellationToken.None);

        Assert.Equal(ComfyUiDetailerSupport.NotSupported, result);
    }

    [Fact]
    public async Task ProbeAsync_OnlyFaceDetailerPresent_ReturnsNotSupported()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, ObjectInfoBody(faceDetailer: true, detectorProvider: false));

        var result = await ComfyUiDetailerProbe.ProbeAsync(new HttpClient(handler), BaseUrl, CancellationToken.None);

        Assert.Equal(ComfyUiDetailerSupport.NotSupported, result);
    }

    [Fact]
    public async Task ProbeAsync_OnlyDetectorProviderPresent_ReturnsNotSupported()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, ObjectInfoBody(faceDetailer: false, detectorProvider: true));

        var result = await ComfyUiDetailerProbe.ProbeAsync(new HttpClient(handler), BaseUrl, CancellationToken.None);

        Assert.Equal(ComfyUiDetailerSupport.NotSupported, result);
    }

    // -----------------------------------------------------------------------
    // 1.2 — transport failure / malformed body → Unknown, never throws
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_NonSuccessStatusCode_ReturnsUnknown()
    {
        var handler = new ScriptedHandler(HttpStatusCode.InternalServerError, "server error");

        var result = await ComfyUiDetailerProbe.ProbeAsync(new HttpClient(handler), BaseUrl, CancellationToken.None);

        Assert.Equal(ComfyUiDetailerSupport.Unknown, result);
    }

    [Fact]
    public async Task ProbeAsync_TransportExceptionThrown_ReturnsUnknown_NoExceptionPropagates()
    {
        var handler = new ThrowingHandler();

        var result = await ComfyUiDetailerProbe.ProbeAsync(new HttpClient(handler), BaseUrl, CancellationToken.None);

        Assert.Equal(ComfyUiDetailerSupport.Unknown, result);
    }

    [Fact]
    public async Task ProbeAsync_MalformedJson_ReturnsUnknown()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, "{not-json");

        var result = await ComfyUiDetailerProbe.ProbeAsync(new HttpClient(handler), BaseUrl, CancellationToken.None);

        Assert.Equal(ComfyUiDetailerSupport.Unknown, result);
    }

    [Fact]
    public async Task ProbeAsync_EmptyBody_ReturnsUnknown()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, string.Empty);

        var result = await ComfyUiDetailerProbe.ProbeAsync(new HttpClient(handler), BaseUrl, CancellationToken.None);

        Assert.Equal(ComfyUiDetailerSupport.Unknown, result);
    }

    [Fact]
    public async Task ProbeAsync_NonObjectRoot_ReturnsUnknown()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, "[]");

        var result = await ComfyUiDetailerProbe.ProbeAsync(new HttpClient(handler), BaseUrl, CancellationToken.None);

        Assert.Equal(ComfyUiDetailerSupport.Unknown, result);
    }

    // -----------------------------------------------------------------------
    // 1.3 — cache TTL behaviour
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_CacheHitWithinTtl_IssuesNoSecondHttpCall()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, ObjectInfoBody(true, true));
        var http = new HttpClient(handler);
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);

        var first = await ComfyUiDetailerProbe.ProbeAsync(http, BaseUrl, time, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(5));
        var second = await ComfyUiDetailerProbe.ProbeAsync(http, BaseUrl, time, CancellationToken.None);

        Assert.Equal(ComfyUiDetailerSupport.Supported, first);
        Assert.Equal(ComfyUiDetailerSupport.Supported, second);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ProbeAsync_CacheExpiredAfterTtl_ReProbes()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, ObjectInfoBody(true, true));
        var http = new HttpClient(handler);
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);

        var first = await ComfyUiDetailerProbe.ProbeAsync(http, BaseUrl, time, CancellationToken.None);
        time.Advance(ComfyUiDetailerProbe.CacheTtl + TimeSpan.FromSeconds(1));
        var second = await ComfyUiDetailerProbe.ProbeAsync(http, BaseUrl, time, CancellationToken.None);

        Assert.Equal(ComfyUiDetailerSupport.Supported, first);
        Assert.Equal(ComfyUiDetailerSupport.Supported, second);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ProbeAsync_TransportFailure_LeavesCacheEmpty_UnknownNeverCached()
    {
        var handler = new ThrowingHandler();
        var http = new HttpClient(handler);
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);

        var first = await ComfyUiDetailerProbe.ProbeAsync(http, BaseUrl, time, CancellationToken.None);
        var second = await ComfyUiDetailerProbe.ProbeAsync(http, BaseUrl, time, CancellationToken.None);

        Assert.Equal(ComfyUiDetailerSupport.Unknown, first);
        Assert.Equal(ComfyUiDetailerSupport.Unknown, second);
        Assert.Equal(2, handler.CallCount);
    }

    // -----------------------------------------------------------------------
    // 1.4 — Invalidate / NotSupported rides out non-TTL signals
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Invalidate_DropsCachedSupported_NextProbeReQueriesHttp()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, ObjectInfoBody(true, true));
        var http = new HttpClient(handler);
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);

        await ComfyUiDetailerProbe.ProbeAsync(http, BaseUrl, time, CancellationToken.None);
        ComfyUiDetailerProbe.Invalidate(BaseUrl);
        await ComfyUiDetailerProbe.ProbeAsync(http, BaseUrl, time, CancellationToken.None);

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ProbeAsync_NotSupportedCached_RidesOutTtlUnaffectedByNonTtlSignals()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, ObjectInfoBody(false, false));
        var http = new HttpClient(handler);
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);

        var first = await ComfyUiDetailerProbe.ProbeAsync(http, BaseUrl, time, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(5));
        var second = await ComfyUiDetailerProbe.ProbeAsync(http, BaseUrl, time, CancellationToken.None);

        Assert.Equal(ComfyUiDetailerSupport.NotSupported, first);
        Assert.Equal(ComfyUiDetailerSupport.NotSupported, second);
        Assert.Equal(1, handler.CallCount);
    }

    // -----------------------------------------------------------------------
    // 1.5 — IsDetailerAttributable
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("""{"node_errors":{"10":{"errors":[{"message":"..."}]}}}""")]
    [InlineData("""{"node_errors":{"11":{"errors":[{"message":"..."}]}}}""")]
    [InlineData("""{"node_errors":{"11":{"class_type":"FaceDetailer"}}}""")]
    [InlineData("""{"error":{"message":"missing node UltralyticsDetectorProvider"}}""")]
    public void IsDetailerAttributable_DetailerNodeErrorOrClassName_ReturnsTrue(string payload)
    {
        Assert.True(ComfyUiDetailerProbe.IsDetailerAttributable(payload));
    }

    [Theory]
    [InlineData("""{"node_errors":{"6":{"errors":[{"message":"ckpt_name not found"}]}}}""")]
    [InlineData("""{"error":{"message":"CUDA out of memory"}}""")]
    [InlineData(null)]
    [InlineData("")]
    public void IsDetailerAttributable_UnrelatedError_ReturnsFalse(string? payload)
    {
        Assert.False(ComfyUiDetailerProbe.IsDetailerAttributable(payload));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string ObjectInfoBody(bool faceDetailer, bool detectorProvider)
    {
        var properties = new List<string>();
        if (faceDetailer)
        {
            properties.Add("""
            "FaceDetailer": { "input": { "required": {} } }
            """);
        }

        if (detectorProvider)
        {
            properties.Add("""
            "UltralyticsDetectorProvider": { "input": { "required": {} } }
            """);
        }

        return "{" + string.Join(",", properties) + "}";
    }

    // -----------------------------------------------------------------------
    // Test doubles
    // -----------------------------------------------------------------------

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public int CallCount { get; private set; }

        public ScriptedHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new HttpRequestException("connection refused");
        }
    }

    /// <summary>Settable <see cref="TimeProvider"/> for deterministic TTL-expiry tests.</summary>
    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public MutableTimeProvider(DateTimeOffset now) => _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
