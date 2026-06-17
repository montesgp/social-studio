using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Export;

/// <summary>
/// Query to export a specific format result as an editable PPTX byte array.
///
/// <para>
/// The handler reads the persisted <c>composition_spec_json</c> from the format-result row
/// and delegates to <c>IPptxExportAdapter</c>. No AI call, no Chromium session.
/// </para>
///
/// <para>Spec: PPTX-02 (download as editable PPTX), PPTX-04 (single read path).</para>
/// </summary>
/// <param name="JobId">The content-generation job that produced the format.</param>
/// <param name="FormatKey">The format key (e.g. "carousel", "story").</param>
/// <param name="OrganizationId">Tenant scope — cross-org job ids return NotFound.</param>
public sealed record ExportFormatToPptxQuery(
    Guid JobId,
    string FormatKey,
    Guid OrganizationId) : IRequest<Result<ExportFormatToPptxResponse>>;

/// <summary>
/// Response carrying the raw PPTX bytes and a suggested download filename.
/// </summary>
/// <param name="PptxBytes">
/// The raw <c>.pptx</c> file bytes. The caller should stream these with
/// <c>application/vnd.openxmlformats-officedocument.presentationml.presentation</c>.
/// </param>
/// <param name="DownloadFilename">
/// Suggested filename for the <c>Content-Disposition</c> header (e.g. <c>"carousel.pptx"</c>).
/// </param>
public sealed record ExportFormatToPptxResponse(
    byte[] PptxBytes,
    string DownloadFilename);
