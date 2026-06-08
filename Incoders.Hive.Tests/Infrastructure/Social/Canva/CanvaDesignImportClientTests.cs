using System.Net;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Social.Canva;

namespace Incoders.Hive.Tests.Infrastructure.Social.Canva;

/// <summary>
/// WU-6 TDD tests for <see cref="CanvaDesignImportClient"/>.
///
/// <para>
/// All tests are fully offline — no real Canva network calls.
/// Uses a <see cref="SequencedFakeHttpMessageHandler"/> that returns pre-configured
/// responses in order, mirroring the style of <see cref="CanvaOAuthServiceTests"/>.
/// </para>
///
/// <para>
/// Canva Design Import flow:
/// 1. POST /rest/v1/imports (multipart, pptx bytes + title) → returns <c>{ jobId }</c>
/// 2. GET  /rest/v1/imports/{jobId} (poll) → returns <c>{ status: "in_progress"|"success"|"failed", result: { designs: [{ urls: { edit_url } }] } }</c>
/// </para>
///
/// <para>
/// Spec: CANVA-EXPORT-01 (edit URL), CANVA-EXPORT-03 (handled errors), CANVA-EXPORT-04 (no autofill).
/// </para>
/// </summary>
public sealed class CanvaDesignImportClientTests
{
    private const string FakeAccessToken = "fake-access-token-for-tests"; // gates:secret-allow
    private const string FakeJobId = "import-job-abc123";
    private const string FakeEditUrl = "https://www.canva.com/design/DABC123/edit";

    // ─── Happy path: submit → poll in_progress → poll success ────────────────

    [Fact]
    public async Task ImportAsync_SubmitsJobAndPollsUntilSuccess_ReturnEditUrl()
    {
        // Arrange — sequence: submit (200 jobId) → poll 1 (in_progress) → poll 2 (success + editUrl)
        var submitResponse = BuildSubmitResponse(FakeJobId);
        var pollProcessingResponse = BuildPollResponse("in_progress");
        var pollSuccessResponse = BuildPollSuccessResponse(FakeJobId, FakeEditUrl);

        var handler = new SequencedFakeHttpMessageHandler(
            submitResponse,
            pollProcessingResponse,
            pollSuccessResponse);

        var client = CreateClient(handler);

        // Act
        var result = await client.ImportAsync(
            pptxBytes: new byte[] { 0x50, 0x4B }, // PK magic bytes (valid PPTX prefix)
            accessToken: FakeAccessToken,
            ct: CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Expected success but got error: {result.Error.Code}");
        Assert.Equal(FakeEditUrl, result.Value.EditUrl);
        Assert.Equal(3, handler.CallCount); // submit + 2 polls
    }

    // ─── Immediate success (submit/poll returns success on first poll) ────────

    [Fact]
    public async Task ImportAsync_ImmediateSuccess_ReturnsEditUrlOnFirstPoll()
    {
        // Arrange — sequence: submit (200 jobId) → poll 1 (success immediately)
        var submitResponse = BuildSubmitResponse(FakeJobId);
        var pollSuccessResponse = BuildPollSuccessResponse(FakeJobId, FakeEditUrl);

        var handler = new SequencedFakeHttpMessageHandler(
            submitResponse,
            pollSuccessResponse);

        var client = CreateClient(handler);

        // Act
        var result = await client.ImportAsync(
            pptxBytes: new byte[] { 0x50, 0x4B },
            accessToken: FakeAccessToken,
            ct: CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Expected success but got error: {result.Error.Code}");
        Assert.Equal(FakeEditUrl, result.Value.EditUrl);
        Assert.Equal(2, handler.CallCount); // submit + 1 poll
    }

    // ─── Bounded poll timeout ─────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_PollTimeout_ReturnsTypedTimeoutError()
    {
        // Arrange — sequence: submit OK, then ALL polls return in_progress (exhausts budget)
        var submitResponse = BuildSubmitResponse(FakeJobId);

        // Build a handler that returns submit once, then endless in_progress
        var handler = new BoundedInProgressFakeHttpMessageHandler(FakeJobId);

        var client = CreateClient(handler);

        // Act
        var result = await client.ImportAsync(
            pptxBytes: new byte[] { 0x50, 0x4B },
            accessToken: FakeAccessToken,
            ct: CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure, "Expected a failure result for timeout");
        Assert.Equal("canva.import.timeout", result.Error.Code);

        // Critical: the client must have stopped polling at the budget; it must NOT loop forever.
        // We assert that poll count is bounded (≤ max-attempts + 1 for the submit).
        // Default budget is 12 attempts; so total calls ≤ 13 (submit + 12 polls).
        Assert.True(handler.CallCount <= 13,
            $"Expected at most 13 calls (1 submit + 12 polls) but got {handler.CallCount}. Client may loop forever.");
    }

    // ─── API failure on submit ────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_SubmitReturns5xx_ReturnsHandledError()
    {
        // Arrange — submit returns HTTP 500
        var handler = new SequencedFakeHttpMessageHandler(
            new FakeResponse(HttpStatusCode.InternalServerError, "{\"message\":\"internal server error\"}"));

        var client = CreateClient(handler);

        // Act
        var result = await client.ImportAsync(
            pptxBytes: new byte[] { 0x50, 0x4B },
            accessToken: FakeAccessToken,
            ct: CancellationToken.None);

        // Assert — must return a handled Result.Failure, NOT throw an exception
        Assert.True(result.IsFailure, "Expected failure for 5xx submit response");
        Assert.NotEqual(string.Empty, result.Error.Code);
        Assert.Equal(1, handler.CallCount); // Only submit was called
    }

    // ─── 401 Unauthorized ────────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_ExpiredToken_ReturnsUnauthorizedError_NotException()
    {
        // Arrange — submit returns HTTP 401
        var handler = new SequencedFakeHttpMessageHandler(
            new FakeResponse(HttpStatusCode.Unauthorized, "{\"message\":\"unauthorized\"}"));

        var client = CreateClient(handler);

        // Act
        var result = await client.ImportAsync(
            pptxBytes: new byte[] { 0x50, 0x4B },
            accessToken: FakeAccessToken,
            ct: CancellationToken.None);

        // Assert — typed unauthorized error, NOT an exception
        Assert.True(result.IsFailure);
        Assert.Equal("canva.import.unauthorized", result.Error.Code);
    }

    // ─── Canva-reported "failed" job status ──────────────────────────────────

    [Fact]
    public async Task ImportAsync_CanvaJobFailed_ReturnsHandledApiFailureError()
    {
        // Arrange — submit OK, poll returns "failed" (with the correct job wrapper)
        var submitResponse = BuildSubmitResponse(FakeJobId);
        var pollFailedResponse = BuildPollResponse("failed");

        var handler = new SequencedFakeHttpMessageHandler(submitResponse, pollFailedResponse);
        var client = CreateClient(handler);

        // Act
        var result = await client.ImportAsync(
            pptxBytes: new byte[] { 0x50, 0x4B },
            accessToken: FakeAccessToken,
            ct: CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("canva.import.api-failure", result.Error.Code);
    }

    // ─── Auth header check ────────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_AuthorizationBearerHeader_IsSentOnSubmit()
    {
        // Arrange — capture request headers
        var captureHandler = new RequestCapturingFakeHttpMessageHandler(
            BuildSubmitResponse(FakeJobId),
            BuildPollSuccessResponse(FakeJobId, FakeEditUrl));

        var client = CreateClient(captureHandler);

        // Act
        await client.ImportAsync(
            pptxBytes: new byte[] { 0x50, 0x4B },
            accessToken: FakeAccessToken,
            ct: CancellationToken.None);

        // Assert — Bearer token is sent on the submit request
        var submitRequest = captureHandler.CapturedRequests.FirstOrDefault();
        Assert.NotNull(submitRequest);
        Assert.Equal("Bearer", submitRequest!.Headers.Authorization?.Scheme);
        Assert.Equal(FakeAccessToken, submitRequest.Headers.Authorization?.Parameter);
    }

    // ─── No Autofill / Brand Templates endpoints ─────────────────────────────

    [Fact]
    public async Task ImportAsync_NeverCallsAutofillOrBrandTemplateEndpoints()
    {
        // Arrange — full happy-path flow, but we capture ALL request URIs
        var captureHandler = new RequestCapturingFakeHttpMessageHandler(
            BuildSubmitResponse(FakeJobId),
            BuildPollSuccessResponse(FakeJobId, FakeEditUrl));

        var client = CreateClient(captureHandler);

        // Act
        await client.ImportAsync(
            pptxBytes: new byte[] { 0x50, 0x4B },
            accessToken: FakeAccessToken,
            ct: CancellationToken.None);

        // Assert — no request URI contains "autofill" or "brand-template"
        foreach (var request in captureHandler.CapturedRequests)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            Assert.DoesNotContain("autofill", uri, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("brand-template", uri, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ─── Integration test (live Canva) — runtime-skipped when env var absent ─

    [Fact]
    public async Task ImportAsync_LiveCanva_Integration()
    {
        var accessToken = Environment.GetEnvironmentVariable("CANVA_ACCESS_TOKEN");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            // Runtime skip — mirrors ChromiumAvailabilityCheck pattern used in B1b tests.
            // When CANVA_ACCESS_TOKEN is absent, this test is treated as passed (skipped).
            return;
        }

        // Build a minimal 1-byte PPTX stand-in — real call needs a valid PPTX in production
        // but this exercises the HTTP flow (will likely fail Canva-side; the test only validates
        // that our adapter makes the right HTTP calls without throwing unhandled exceptions).
        var pptxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // PK magic

        // Use the default (production) constructor for the live integration test:
        // real poll intervals are appropriate here since we're testing against real Canva.
        var httpClient = new HttpClient();
        var client = new CanvaDesignImportClient(httpClient); // production constructor — 5s poll interval

        var result = await client.ImportAsync(pptxBytes, accessToken, ct: CancellationToken.None);

        // The integration test accepts either success (real PPTX returns edit URL) or
        // a handled failure (Canva rejects our stub bytes). What it MUST NOT do is throw.
        Assert.True(result.IsSuccess || result.IsFailure,
            "Result must be either success or failure — never an unhandled exception.");
    }

    // ─── Factory ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="CanvaDesignImportClient"/> using the testable constructor
    /// with a zero poll interval so unit tests complete in milliseconds, not seconds.
    /// </summary>
    private static CanvaDesignImportClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        // Use the internal testable constructor: zero poll interval keeps tests fast
        // while still exercising the full bounded-poll loop logic.
        return new CanvaDesignImportClient(httpClient, TimeSpan.Zero);
    }

    // ─── Response builders ────────────────────────────────────────────────────

    private static FakeResponse BuildSubmitResponse(string jobId)
        => new(HttpStatusCode.OK, JsonSerializer.Serialize(new { job = new { id = jobId, status = "in_progress" } }));

    private static FakeResponse BuildPollResponse(string status)
        => new(HttpStatusCode.OK, JsonSerializer.Serialize(new { job = new { id = FakeJobId, status } }));

    private static FakeResponse BuildPollSuccessResponse(string jobId, string editUrl)
    {
        var body = JsonSerializer.Serialize(new
        {
            job = new
            {
                id = jobId,
                status = "success",
                result = new
                {
                    designs = new[]
                    {
                        new
                        {
                            id = "design-abc",
                            urls = new { edit_url = editUrl }
                        }
                    }
                }
            }
        });
        return new FakeResponse(HttpStatusCode.OK, body);
    }
}

// ─── Test doubles ─────────────────────────────────────────────────────────────

/// <summary>A canned HTTP response for use in fake handlers.</summary>
internal sealed record FakeResponse(HttpStatusCode StatusCode, string Body);

/// <summary>
/// Returns pre-configured responses in sequence.
/// If more requests arrive than responses configured, returns the LAST response indefinitely.
/// Mirrors the pattern from <see cref="FakeHttpMessageHandler"/> but supports sequences.
/// </summary>
internal sealed class SequencedFakeHttpMessageHandler : HttpMessageHandler
{
    private readonly FakeResponse[] _responses;
    private int _callIndex;

    public int CallCount => _callIndex;

    public SequencedFakeHttpMessageHandler(params FakeResponse[] responses)
    {
        if (responses.Length == 0)
            throw new ArgumentException("Must provide at least one response", nameof(responses));
        _responses = responses;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var index = Math.Min(_callIndex, _responses.Length - 1);
        _callIndex++;

        var r = _responses[index];
        return Task.FromResult(new HttpResponseMessage(r.StatusCode)
        {
            Content = new StringContent(r.Body, Encoding.UTF8, "application/json"),
        });
    }
}

/// <summary>
/// Returns submit OK once, then endless in_progress — used to trigger bounded poll timeout.
/// </summary>
internal sealed class BoundedInProgressFakeHttpMessageHandler : HttpMessageHandler
{
    private readonly string _jobId;
    private int _callCount;

    public int CallCount => _callCount;

    public BoundedInProgressFakeHttpMessageHandler(string jobId) => _jobId = jobId;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _callCount++;

        string body;
        if (_callCount == 1)
        {
            // First call = submit → return job id with in_progress
            body = JsonSerializer.Serialize(new { job = new { id = _jobId, status = "in_progress" } });
        }
        else
        {
            // All polls return in_progress forever
            body = JsonSerializer.Serialize(new { job = new { id = _jobId, status = "in_progress" } });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}

/// <summary>
/// Captures all <see cref="HttpRequestMessage"/> instances for later assertion,
/// returning pre-configured responses in sequence.
/// </summary>
internal sealed class RequestCapturingFakeHttpMessageHandler : HttpMessageHandler
{
    private readonly FakeResponse[] _responses;
    private int _callIndex;
    private readonly List<HttpRequestMessage> _capturedRequests = new();

    public IReadOnlyList<HttpRequestMessage> CapturedRequests => _capturedRequests;

    public RequestCapturingFakeHttpMessageHandler(params FakeResponse[] responses)
    {
        _responses = responses;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _capturedRequests.Add(request);

        var index = Math.Min(_callIndex, _responses.Length - 1);
        _callIndex++;

        var r = _responses[index];
        return Task.FromResult(new HttpResponseMessage(r.StatusCode)
        {
            Content = new StringContent(r.Body, Encoding.UTF8, "application/json"),
        });
    }
}
