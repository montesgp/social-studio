using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Infrastructure.Social.Canva;

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

    // Poll budget: 12 attempts × 5 s = 60 s max before returning a typed timeout error.
    // Kept conservative to avoid hammering the rate limit (~20 req/min/user).
    private const int MaxPollAttempts = 12;
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _pollInterval;

    /// <summary>
    /// Production constructor — uses the default 5-second poll interval.
    /// DI registers this via <c>AddHttpClient&lt;ICanvaDesignImportClient, CanvaDesignImportClient&gt;</c>.
    /// </summary>
    public CanvaDesignImportClient(HttpClient httpClient)
        : this(httpClient, DefaultPollInterval)
    {
    }

    /// <summary>
    /// Testable constructor — accepts a custom poll interval to keep unit tests fast.
    /// Pass <see cref="TimeSpan.Zero"/> or a very short value in tests to avoid 5-second delays.
    /// </summary>
    internal CanvaDesignImportClient(HttpClient httpClient, TimeSpan pollInterval)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _pollInterval = pollInterval;
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
        using var content = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(pptxBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.presentationml.presentation");
        // Canva titles the imported design from the upload filename — use the caller's title.
        var fileName = $"{SanitizeFileName(designTitle) ?? "presentation"}.pptx";
        content.Add(fileContent, "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, CanvaImportUrl)
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result.Failure<string>(CanvaImportErrors.ApiFailure);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            return Result.Failure<string>(CanvaImportErrors.Unauthorized);

        if (!response.IsSuccessStatusCode)
            return Result.Failure<string>(CanvaImportErrors.ApiFailure);

        var json = await response.Content.ReadAsStringAsync(ct);

        CanvaImportJobResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<CanvaImportJobResponse>(json);
        }
        catch
        {
            return Result.Failure<string>(CanvaImportErrors.ApiFailure);
        }

        var jobId = parsed?.Job?.Id;
        if (string.IsNullOrWhiteSpace(jobId))
            return Result.Failure<string>(CanvaImportErrors.ApiFailure);

        return Result.Success(jobId);
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

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return Result.Failure<CanvaImportResult>(CanvaImportErrors.Unauthorized);

            if (!response.IsSuccessStatusCode)
                return Result.Failure<CanvaImportResult>(CanvaImportErrors.ApiFailure);

            var json = await response.Content.ReadAsStringAsync(ct);

            CanvaImportJobResponse? pollResult;
            try
            {
                pollResult = JsonSerializer.Deserialize<CanvaImportJobResponse>(json);
            }
            catch
            {
                return Result.Failure<CanvaImportResult>(CanvaImportErrors.ApiFailure);
            }

            var status = pollResult?.Job?.Status;

            if (status == "success")
            {
                var editUrl = pollResult?.Job?.Result?.Designs?.FirstOrDefault()?.Urls?.EditUrl;
                var designId = pollResult?.Job?.Result?.Designs?.FirstOrDefault()?.Id ?? string.Empty;

                if (string.IsNullOrWhiteSpace(editUrl))
                    return Result.Failure<CanvaImportResult>(CanvaImportErrors.ApiFailure);

                var expiresAt = DateTimeOffset.UtcNow.AddDays(30);
                return Result.Success(new CanvaImportResult(designId, editUrl, expiresAt));
            }

            if (status == "failed")
                return Result.Failure<CanvaImportResult>(CanvaImportErrors.ApiFailure);

            // status == "in_progress" — continue polling
        }

        // Exhausted all attempts without success
        return Result.Failure<CanvaImportResult>(CanvaImportErrors.Timeout);
    }

    /// <summary>
    /// Reduces a free-form title to a safe upload filename stem (Canva derives the design name
    /// from it). Strips path/quote characters and clamps length. Returns null when blank.
    /// </summary>
    private static string? SanitizeFileName(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var cleaned = new string(title
            .Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '-')
            .ToArray())
            .Trim();

        if (cleaned.Length > 80) cleaned = cleaned[..80].Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
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
