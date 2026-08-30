using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Export;

/// <summary>
/// Handles <see cref="ExportFormatToCanvaCommand"/>.
///
/// <para>Algorithm:</para>
/// <list type="number">
///   <item><description>Tenant-scoped job lookup (cross-org → not-found).</description></item>
///   <item><description>Load format result; find the row matching <c>FormatKey</c>.</description></item>
///   <item><description>If <c>CompositionSpecJson</c> is absent → return <c>not-exportable</c>.</description></item>
///   <item><description>Lookup per-user Canva token; if absent → return <c>canva.not_connected</c>.</description></item>
///   <item><description>Call <c>EnsureFreshAsync</c> with the REAL <c>organizationId</c> (D2 fix).</description></item>
///   <item><description>Build PPTX bytes via <see cref="IPptxExportAdapter"/>.</description></item>
///   <item><description>Import PPTX via <see cref="ICanvaDesignImportClient"/>.</description></item>
///   <item><description>On import 401 → revoke stale token + return <c>canva.reconnect_required</c>.</description></item>
///   <item><description>Return <see cref="ExportFormatToCanvaResponse"/> with the edit URL.</description></item>
/// </list>
///
/// <para>Spec: CANVA-EXPORT-01, CANVA-EXPORT-02, CANVA-EXPORT-03, CANVA-USER-03.</para>
/// </summary>
public sealed class ExportFormatToCanvaCommandHandler
    : IRequestHandler<ExportFormatToCanvaCommand, Result<ExportFormatToCanvaResponse>>
{
    private const string UnauthorizedImportErrorCode = "canva.import.unauthorized";

    private readonly IContentGenerationJobRepository _jobRepository;
    private readonly IContentGenerationFormatResultRepository _formatResultRepository;
    private readonly ICanvaUserTokenRepository _tokenRepository;
    private readonly ICanvaOAuthService _oauthService;
    private readonly IPptxExportAdapter _pptxAdapter;
    private readonly ICanvaDesignImportClient _importClient;

    public ExportFormatToCanvaCommandHandler(
        IContentGenerationJobRepository jobRepository,
        IContentGenerationFormatResultRepository formatResultRepository,
        ICanvaUserTokenRepository tokenRepository,
        ICanvaOAuthService oauthService,
        IPptxExportAdapter pptxAdapter,
        ICanvaDesignImportClient importClient)
    {
        ArgumentNullException.ThrowIfNull(jobRepository);
        ArgumentNullException.ThrowIfNull(formatResultRepository);
        ArgumentNullException.ThrowIfNull(tokenRepository);
        ArgumentNullException.ThrowIfNull(oauthService);
        ArgumentNullException.ThrowIfNull(pptxAdapter);
        ArgumentNullException.ThrowIfNull(importClient);

        _jobRepository = jobRepository;
        _formatResultRepository = formatResultRepository;
        _tokenRepository = tokenRepository;
        _oauthService = oauthService;
        _pptxAdapter = pptxAdapter;
        _importClient = importClient;
    }

    public async Task<Result<ExportFormatToCanvaResponse>> Handle(
        ExportFormatToCanvaCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Tenant-scoped job lookup
        var job = await _jobRepository.GetByIdAsync(
            command.JobId,
            command.OrganizationId,
            cancellationToken);

        if (job is null)
        {
            return Result.Failure<ExportFormatToCanvaResponse>(
                ContentGenerationErrors.JobNotFound);
        }

        // 2. Load format results for this job and find the requested format
        var formatResults = await _formatResultRepository.ListByJobAsync(
            command.JobId,
            cancellationToken);

        var formatResult = formatResults.FirstOrDefault(r =>
            string.Equals(r.FormatKey, command.FormatKey, StringComparison.OrdinalIgnoreCase));

        if (formatResult is null)
        {
            return Result.Failure<ExportFormatToCanvaResponse>(
                Error.NotFound(
                    "content-gen.format-not-found",
                    $"No stored result found for format '{command.FormatKey}'."));
        }

        // 3. Verify markup is persisted
        if (string.IsNullOrWhiteSpace(formatResult.CompositionSpecJson))
        {
            return Result.Failure<ExportFormatToCanvaResponse>(
                ContentGenerationErrors.NotExportable);
        }

        // 4. Parse the persisted envelope(s): one per slide for carousel, one for single formats.
        //    Accepts both the legacy single-object shape and the multi-slide array shape.
        var envelopes = CompositionSpecJson.ParseActiveList(formatResult.CompositionSpecJson);
        if (envelopes.Count == 0)
        {
            return Result.Failure<ExportFormatToCanvaResponse>(
                ContentGenerationErrors.NotExportable);
        }

        // 5. Check that the user has connected their Canva account
        // Org-level connection: look up the shared org token, not a per-user one.
        var existingToken = await _tokenRepository.GetAsync(
            CanvaOrgConnection.SharedUserId,
            command.OrganizationIntegrationId,
            cancellationToken);

        if (existingToken is null)
        {
            // User has not completed the OAuth flow — return a handled failure the UI
            // can detect to trigger the Connect flow.
            return Result.Failure<ExportFormatToCanvaResponse>(
                CanvaExportErrors.NotConnected);
        }

        // 6. Ensure the access token is fresh (D2 rotation if expired).
        //    Pass the REAL organizationId — not the organizationIntegrationId proxy.
        var tokenResult = await _oauthService.EnsureFreshAsync(
            CanvaOrgConnection.SharedUserId,
            command.OrganizationIntegrationId,
            command.OrganizationId,
            cancellationToken);

        if (tokenResult.IsFailure)
        {
            return Result.Failure<ExportFormatToCanvaResponse>(tokenResult.Error);
        }

        var accessToken = tokenResult.Value;

        // 7. Build PPTX bytes (offline, deterministic) — one slide per envelope.
        var buildResult = await _pptxAdapter.BuildManyAsync(envelopes, cancellationToken);
        if (buildResult.IsFailure)
        {
            return Result.Failure<ExportFormatToCanvaResponse>(buildResult.Error);
        }

        // 8. Import PPTX into Canva (title the design after the format).
        var importResult = await _importClient.ImportAsync(
            buildResult.Value, accessToken, designTitle: command.FormatKey, ct: cancellationToken);

        if (importResult.IsFailure)
        {
            // Stale/revoked access token — clean up and ask user to reconnect
            if (string.Equals(importResult.Error.Code, UnauthorizedImportErrorCode, StringComparison.OrdinalIgnoreCase))
            {
                await _oauthService.RevokeAsync(
                    CanvaOrgConnection.SharedUserId,
                    command.OrganizationIntegrationId,
                    cancellationToken);

                return Result.Failure<ExportFormatToCanvaResponse>(
                    CanvaExportErrors.ReconnectRequired);
            }

            return Result.Failure<ExportFormatToCanvaResponse>(importResult.Error);
        }

        var canvaResult = importResult.Value;

        return Result.Success(new ExportFormatToCanvaResponse(
            DesignId: canvaResult.DesignId,
            EditUrl: canvaResult.EditUrl,
            ExpiresAt: canvaResult.ExpiresAt));
    }
}

/// <summary>Export-to-Canva domain errors specific to WU-7.</summary>
public static class CanvaExportErrors
{
    /// <summary>
    /// The user has not connected their Canva account (no per-user token stored).
    /// Spec CANVA-EXPORT-02: return a handled failure so the UI can prompt the Connect flow.
    /// Maps to HTTP 409 Conflict (the resource exists but is not in the required state).
    /// </summary>
    public static readonly Error NotConnected = Error.Conflict(
        "canva.not_connected",
        "User has not connected their Canva account. Complete the OAuth flow first.");

    /// <summary>
    /// The Canva API returned 401 — the stored token was revoked or expired server-side.
    /// The token has been deleted; the user must reconnect.
    /// Maps to HTTP 409 Conflict.
    /// </summary>
    public static readonly Error ReconnectRequired = Error.Conflict(
        "canva.reconnect_required",
        "Canva access token was rejected. Please reconnect your Canva account.");
}
