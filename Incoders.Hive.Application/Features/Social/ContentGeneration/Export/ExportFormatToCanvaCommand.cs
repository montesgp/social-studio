using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Export;

/// <summary>
/// Command to export a specific format result to Canva as an editable design.
///
/// <para>
/// The handler:
/// <list type="number">
///   <item><description>Verifies the job belongs to the tenant (cross-org = NotFound).</description></item>
///   <item><description>Loads the persisted <c>composition_spec_json</c>; if absent → <c>not-exportable</c>.</description></item>
///   <item><description>Checks that the user has a Canva token; if absent → <c>canva.not_connected</c>.</description></item>
///   <item><description>Calls <c>ICanvaOAuthService.EnsureFreshAsync</c> with the REAL <c>organizationId</c>.</description></item>
///   <item><description>Builds the PPTX via <c>IPptxExportAdapter</c>.</description></item>
///   <item><description>Imports the PPTX via <c>ICanvaDesignImportClient</c>.</description></item>
///   <item><description>On 401 from import → deletes token + returns <c>canva.reconnect_required</c>.</description></item>
///   <item><description>Returns <see cref="ExportFormatToCanvaResponse"/> with the edit URL.</description></item>
/// </list>
/// </para>
///
/// <para>Spec: CANVA-EXPORT-01 (edit URL), CANVA-EXPORT-02 (not connected → handled), CANVA-EXPORT-03 (API failures).</para>
/// </summary>
/// <param name="JobId">The content-generation job that produced the format.</param>
/// <param name="FormatKey">The format key (e.g. "carousel", "story").</param>
/// <param name="OrganizationId">Tenant scope — cross-org job ids return NotFound.</param>
/// <param name="OrganizationIntegrationId">The org-level Canva integration row id used to look up the per-user token.</param>
/// <param name="UserId">The authenticated user requesting the export.</param>
public sealed record ExportFormatToCanvaCommand(
    Guid JobId,
    string FormatKey,
    Guid OrganizationId,
    Guid OrganizationIntegrationId,
    Guid UserId) : IRequest<Result<ExportFormatToCanvaResponse>>;

/// <summary>
/// Response carrying the Canva edit URL and design metadata.
/// </summary>
/// <param name="DesignId">The Canva design identifier returned by the Design Import API.</param>
/// <param name="EditUrl">
/// The direct edit URL for the imported design in Canva.
/// The UI should open this in a new tab.
/// </param>
/// <param name="ExpiresAt">Approximate expiry of the edit URL (~30 days from import).</param>
public sealed record ExportFormatToCanvaResponse(
    string DesignId,
    string EditUrl,
    DateTimeOffset ExpiresAt);
