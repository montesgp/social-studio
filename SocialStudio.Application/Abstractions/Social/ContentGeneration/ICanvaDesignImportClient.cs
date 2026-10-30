using SocialStudio.Domain.Common;

namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port — imports a PPTX file into Canva via the Canva Connect Design Import API,
/// polls the async import job, and returns the edit URL for the resulting Canva design.
///
/// <para>
/// The implementation lives in Infrastructure (Canva HTTP adapter). This interface is
/// intentionally provider-neutral: it accepts raw <c>byte[]</c> (not a stream or file path),
/// a bearer access token as a plain <c>string</c>, and returns a domain result record.
/// No <c>HttpClient</c>, JSON, or Canva SDK types are present in this signature.
/// </para>
///
/// <para>
/// Import flow (Canva Connect API):
/// <list type="number">
///   <item><description>
///     POST <c>/rest/v1/imports</c> (multipart, <c>file=pptx</c>) →
///     returns an async job <c>{ id, status: "in_progress" }</c>.
///   </description></item>
///   <item><description>
///     Poll GET <c>/rest/v1/imports/{jobId}</c> until <c>status == "success"</c>
///     (returns edit URL) or <c>"failed"</c>, within a bounded poll budget
///     (default: 12 attempts × 5 s = 60 s max).
///   </description></item>
/// </list>
/// </para>
///
/// <para>
/// Spec: CANVA-EXPORT-01 (edit URL via Design Import),
///       CANVA-EXPORT-03 (API failures → typed <see cref="Result"/> errors),
///       CANVA-EXPORT-04 (Autofill and Brand Templates NEVER called from this path).
/// </para>
/// </summary>
public interface ICanvaDesignImportClient
{
    /// <summary>
    /// Submits the PPTX bytes to Canva's Design Import API, polls for completion,
    /// and returns the edit URL for the resulting design.
    /// </summary>
    /// <param name="pptxBytes">
    /// The raw PPTX file bytes to import. Must not be null or empty.
    /// </param>
    /// <param name="accessToken">
    /// A valid Canva per-user access token (Bearer). Must not be null or whitespace.
    /// Obtain via <c>ICanvaOAuthService.EnsureFreshAsync</c>.
    /// </param>
    /// <param name="ct">Cancellation token. Honored during polling.</param>
    /// <returns>
    /// <c>Result.Success(<see cref="CanvaImportResult"/>)</c> when the import job completes
    /// successfully and an edit URL is available.
    ///
    /// <c>Result.Failure</c> with one of the following error codes for known failure modes:
    /// <list type="bullet">
    ///   <item><description><c>"canva.import.unauthorized"</c> — access token is invalid or expired (HTTP 401).</description></item>
    ///   <item><description><c>"canva.import.api-failure"</c> — Canva returned a non-2xx response or the import job reported <c>"failed"</c>.</description></item>
    ///   <item><description><c>"canva.import.timeout"</c> — the import job did not complete within the bounded poll budget.</description></item>
    /// </list>
    ///
    /// Expected failures NEVER throw; only truly unexpected conditions (e.g. network infrastructure failure)
    /// may propagate as exceptions, which callers must handle defensively.
    /// </returns>
    /// <param name="designTitle">
    /// Optional human-friendly title for the imported design (Canva uses the upload filename as the
    /// design name). When null/blank a generic default is used. Used to name a package design after
    /// the client.
    /// </param>
    Task<Result<CanvaImportResult>> ImportAsync(
        byte[] pptxBytes,
        string accessToken,
        string? designTitle = null,
        CancellationToken ct = default);
}
