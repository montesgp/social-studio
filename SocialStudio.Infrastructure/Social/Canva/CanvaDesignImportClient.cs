using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Infrastructure.Social.Canva;

/// <summary>
/// Infrastructure adapter: submits a PPTX file to the Canva Connect Design Import API,
/// polls the async import job with a bounded budget, and returns the edit URL.
///
/// <para>
/// Design Import flow:
/// 1. POST <c>https://api.canva.com/rest/v1/imports</c> (multipart/form-data, <c>file=pptx</c>)
///    → async job <c>{ job: { id, status: "in_progress" } }</c>.
/// 2. Poll GET <c>https://api.canva.com/rest/v1/imports/{jobId}</c> until
///    <c>status == "success"</c> (edit URL) or <c>"failed"</c>,
///    up to <see cref="MaxPollAttempts"/> attempts with <see cref="PollInterval"/> delay.
/// </para>
///
/// <para>
/// Bounded poll budget: default 12 attempts × 5 s = 60 s max.
/// Rate-limit headroom (~20 req/min/user): 12 polls at 5 s spacing = 2.4 req/min, well within budget.
/// </para>
///
/// <para>
/// The adapter never calls <c>/autofill</c> or <c>/brand-templates</c> endpoints.
/// All expected failure modes return a <see cref="Result{T}"/> failure — no exceptions for
/// known conditions (401, 4xx/5xx, job "failed", poll timeout).
/// </para>
///
/// <para>
/// Registered via <c>AddHttpClient&lt;ICanvaDesignImportClient, CanvaDesignImportClient&gt;</c>
/// so the DI-injected <see cref="HttpClient"/> carries the typed registration name <c>"canva-import"</c>.
/// </para>
/// </summary>
public sealed class CanvaDesignImportClient : ICanvaDesignImportClient
{
    // Canva Connect API base URL (verified against Canva Connect API docs, 2026)
    private const string CanvaImportUrl = "https://api.canva.com/rest/v1/imports";

    private const string PptxMimeType =
        "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    // Poll budget: 12 attempts × 5 s = 60 s max before returning a typed timeout error.
    // Kept conservative to avoid hammering the rate limit (~20 req/min/user).
    private const int MaxPollAttempts = 12;
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _pollInterval;
    private readonly ILogger<CanvaDesignImportClient> _logger;

    /// <summary>
    /// Production constructor — uses the default 5-second poll interval.
    /// DI registers this via <c>AddHttpClient&lt;ICanvaDesignImportClient, CanvaDesignImportClient&gt;</c>.
    /// </summary>
    public CanvaDesignImportClient(HttpClient httpClient, ILogger<CanvaDesignImportClient>? logger = null)
        : this(httpClient, DefaultPollInterval, logger)
    {
    }

    /// <summary>
    /// Testable constructor — accepts a custom poll interval to keep unit tests fast.
    /// Pass <see cref="TimeSpan.Zero"/> or a very short value in tests to avoid 5-second delays.
    /// </summary>
    internal CanvaDesignImportClient(HttpClient httpClient, TimeSpan pollInterval, ILogger<CanvaDesignImportClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _pollInterval = pollInterval;
        _logger = logger ?? NullLogger<CanvaDesignImportClient>.Instance;
    }

    /// <inheritdoc/>
    public async Task<Result<CanvaImportResult>> ImportAsync(
        byte[] pptxBytes,
        string accessToken,
        string? designTitle = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pptxBytes);
        if (string.IsNullOrWhiteSpace(accessToken))
            return Result.Failure<CanvaImportResult>(CanvaImportErrors.Unauthorized);

        // ── Step 1: Submit the PPTX via multipart/form-data ──────────────────
        var submitResult = await SubmitImportAsync(pptxBytes, accessToken, designTitle, ct);
        if (submitResult.IsFailure)
            return Result.Failure<CanvaImportResult>(submitResult.Error);

        var jobId = submitResult.Value;

        // ── Step 2: Poll for job completion within bounded budget ─────────────
        return await PollJobAsync(jobId, accessToken, ct);
    }

    // ─── Private: submit ─────────────────────────────────────────────────────

    private async Task<Result<string>> SubmitImportAsync(
        byte[] pptxBytes,
        string accessToken,
        string? designTitle,
        CancellationToken ct)
    {
        // Canva Design Import expects a RAW application/octet-stream body (the file bytes) plus an
        // Import-Metadata header — NOT multipart/form-data. Sending multipart makes Canva reject the
        // request, which surfaces to the user as a 502 on export.
        // Docs: https://www.canva.dev/docs/connect/api-reference/design-imports/create-design-import-job/
        var fileContent = new ByteArrayContent(pptxBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var request = new HttpRequestMessage(HttpMethod.Post, CanvaImportUrl)
        {
            Content = fileContent,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        // Import-Metadata carries the (Base64-encoded) design title + MIME type. Added without
        // validation because the JSON value contains characters HttpClient would otherwise reject.
        request.Headers.TryAddWithoutValidation("Import-Metadata", BuildImportMetadata(designTitle));

        _logger.LogInformation(
            "Canva import SUBMIT: POST {Url} ({Bytes} bytes, octet-stream).", CanvaImportUrl, pptxBytes.Length);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Canva import SUBMIT threw (network/cancel).");
            return Result.Failure<string>(CanvaImportErrors.ApiFailure);
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Canva import SUBMIT unauthorized (HTTP 401). Body: {Body}", Truncate(body));
            return Result.Failure<string>(CanvaImportErrors.Unauthorized);
        }

        if (!response.IsSuccessStatusCode)
        {
            // THIS is the line that finally surfaces why Canva rejects the import (e.g. bad scope,
            // unsupported file, malformed Import-Metadata). Previously swallowed into an opaque 502.
            _logger.LogWarning("Canva import SUBMIT failed: HTTP {Status}. Body: {Body}",
                (int)response.StatusCode, Truncate(body));
            return Result.Failure<string>(CanvaImportErrors.ApiFailure);
        }

        CanvaImportJobResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<CanvaImportJobResponse>(body);
        }
        catch
        {
            _logger.LogWarning("Canva import SUBMIT: could not parse response. Body: {Body}", Truncate(body));
            return Result.Failure<string>(CanvaImportErrors.ApiFailure);
        }

        var jobId = parsed?.Job?.Id;
        if (string.IsNullOrWhiteSpace(jobId))
        {
            _logger.LogWarning("Canva import SUBMIT: response had no job id. Body: {Body}", Truncate(body));
            return Result.Failure<string>(CanvaImportErrors.ApiFailure);
        }

        _logger.LogInformation("Canva import SUBMIT accepted. Job {JobId}.", jobId);
        return Result.Success(jobId);
    }

    private static string Truncate(string? value, int max = 1000)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..max] + "…";
    }

    // ─── Private: poll ───────────────────────────────────────────────────────

    private async Task<Result<CanvaImportResult>> PollJobAsync(
        string jobId,
        string accessToken,
        CancellationToken ct)
    {
        var pollUrl = $"{CanvaImportUrl}/{Uri.EscapeDataString(jobId)}";

        for (var attempt = 0; attempt < MaxPollAttempts; attempt++)
        {
            // Respect cancellation before each poll attempt
            if (ct.IsCancellationRequested)
                return Result.Failure<CanvaImportResult>(CanvaImportErrors.ApiFailure);

            // Wait between polls (skip delay on first iteration so immediate-success is fast)
            if (attempt > 0 && _pollInterval > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(_pollInterval, ct);
                }
                catch (TaskCanceledException)
                {
                    return Result.Failure<CanvaImportResult>(CanvaImportErrors.ApiFailure);
                }
            }

            var pollRequest = new HttpRequestMessage(HttpMethod.Get, pollUrl);
            pollRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(pollRequest, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return Result.Failure<CanvaImportResult>(CanvaImportErrors.ApiFailure);
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Canva import POLL unauthorized (HTTP 401). Body: {Body}", Truncate(body));
                return Result.Failure<CanvaImportResult>(CanvaImportErrors.Unauthorized);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Canva import POLL failed: HTTP {Status}. Body: {Body}",
                    (int)response.StatusCode, Truncate(body));
                return Result.Failure<CanvaImportResult>(CanvaImportErrors.ApiFailure);
            }

            CanvaImportJobResponse? pollResult;
            try
            {
                pollResult = JsonSerializer.Deserialize<CanvaImportJobResponse>(body);
            }
            catch
            {
                _logger.LogWarning("Canva import POLL: could not parse response. Body: {Body}", Truncate(body));
                return Result.Failure<CanvaImportResult>(CanvaImportErrors.ApiFailure);
            }

            var status = pollResult?.Job?.Status;

            if (status == "success")
            {
                var editUrl = pollResult?.Job?.Result?.Designs?.FirstOrDefault()?.Urls?.EditUrl;
                var designId = pollResult?.Job?.Result?.Designs?.FirstOrDefault()?.Id ?? string.Empty;

                if (string.IsNullOrWhiteSpace(editUrl))
                {
                    _logger.LogWarning("Canva import POLL: status success but no edit_url. Body: {Body}", Truncate(body));
                    return Result.Failure<CanvaImportResult>(CanvaImportErrors.ApiFailure);
                }

                _logger.LogInformation("Canva import SUCCESS. Design {DesignId}.", designId);
                var expiresAt = DateTimeOffset.UtcNow.AddDays(30);
                return Result.Success(new CanvaImportResult(designId, editUrl, expiresAt));
            }

            if (status == "failed")
            {
                // Canva reports the failure reason in the job body — log it verbatim.
                _logger.LogWarning("Canva import job reported FAILED. Body: {Body}", Truncate(body));
                return Result.Failure<CanvaImportResult>(CanvaImportErrors.ApiFailure);
            }

            // status == "in_progress" — continue polling
        }

        // Exhausted all attempts without success
        _logger.LogWarning("Canva import POLL exhausted budget ({Attempts} attempts) without completion.", MaxPollAttempts);
        return Result.Failure<CanvaImportResult>(CanvaImportErrors.Timeout);
    }

    /// <summary>
    /// Builds the <c>Import-Metadata</c> header value: a compact JSON object with the Base64-encoded
    /// design title and the file MIME type. Base64 lets titles with emojis/accents ride an HTTP header.
    /// Canva caps the unencoded title at 50 characters.
    /// </summary>
    private static string BuildImportMetadata(string? designTitle)
    {
        var title = string.IsNullOrWhiteSpace(designTitle) ? "presentation" : designTitle.Trim();
        if (title.Length > 50) title = title[..50].Trim();
        var titleBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(title));
        return $"{{\"title_base64\":\"{titleBase64}\",\"mime_type\":\"{PptxMimeType}\"}}";
    }

    // ─── Private response DTOs ────────────────────────────────────────────────
    // These are Infrastructure-internal: they map the Canva Connect API JSON shape
    // and are never exposed through the Application port.

    private sealed class CanvaImportJobResponse
    {
        [JsonPropertyName("job")]
        public CanvaImportJobBody? Job { get; init; }
    }

    private sealed class CanvaImportJobBody
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("result")]
        public CanvaImportJobResult? Result { get; init; }
    }

    private sealed class CanvaImportJobResult
    {
        [JsonPropertyName("designs")]
        public CanvaImportDesign[]? Designs { get; init; }
    }

    private sealed class CanvaImportDesign
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("urls")]
        public CanvaImportDesignUrls? Urls { get; init; }
    }

    private sealed class CanvaImportDesignUrls
    {
        [JsonPropertyName("edit_url")]
        public string? EditUrl { get; init; }
    }
}

/// <summary>Canva Design Import domain errors.</summary>
public static class CanvaImportErrors
{
    /// <summary>Access token is invalid or expired (HTTP 401 from Canva).</summary>
    public static readonly Error Unauthorized = Error.Failure(
        "canva.import.unauthorized",
        "canva.errors.import.unauthorized");

    /// <summary>Canva API returned a non-2xx response or the import job reported "failed".</summary>
    public static readonly Error ApiFailure = Error.Failure(
        "canva.import.api-failure",
        "canva.errors.import.api_failure");

    /// <summary>Import job did not complete within the bounded poll budget (12 attempts × 5 s).</summary>
    public static readonly Error Timeout = Error.Failure(
        "canva.import.timeout",
        "canva.errors.import.timeout");
}
