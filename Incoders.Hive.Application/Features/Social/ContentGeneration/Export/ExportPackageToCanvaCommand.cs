using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Export;

/// <summary>
/// Command to export the whole VISUAL package of a content-generation job to Canva as a single
/// editable multi-page design (one "Send to Canva" for the package). The story and every carousel
/// slide become editable pages; the design is titled after the client.
///
/// <para>
/// Scope: job-level — it aggregates the visual formats produced by <paramref name="JobId"/>
/// (the natural "Convert to all formats" flow). Text formats (reel-script, etc.) are not visual
/// compositions and are excluded.
/// </para>
/// </summary>
/// <param name="JobId">The content-generation job whose visual formats are exported.</param>
/// <param name="OrganizationId">Tenant scope — cross-org job ids return NotFound.</param>
/// <param name="OrganizationIntegrationId">Org-level Canva integration row id used to find the per-user token.</param>
/// <param name="UserId">The authenticated user requesting the export.</param>
public sealed record ExportPackageToCanvaCommand(
    Guid JobId,
    Guid OrganizationId,
    Guid OrganizationIntegrationId,
    Guid UserId) : IRequest<Result<ExportFormatToCanvaResponse>>;

/// <summary>
/// Handles <see cref="ExportPackageToCanvaCommand"/>. Mirrors the per-format export but aggregates
/// EVERY visual format of the job into one multi-page PPTX before importing to Canva.
/// </summary>
public sealed class ExportPackageToCanvaCommandHandler
    : IRequestHandler<ExportPackageToCanvaCommand, Result<ExportFormatToCanvaResponse>>
{
    private const string UnauthorizedImportErrorCode = "canva.import.unauthorized";

    private readonly IContentGenerationJobRepository _jobRepository;
    private readonly IContentGenerationFormatResultRepository _formatResultRepository;
    private readonly IGeneratedPackageRepository? _packageRepository;
    private readonly ICanvaUserTokenRepository _tokenRepository;
    private readonly ICanvaOAuthService _oauthService;
    private readonly IPptxExportAdapter _pptxAdapter;
    private readonly ICanvaDesignImportClient _importClient;
    private readonly ISocialClientRepository _clientRepository;
    private readonly IClock? _clock;
    private readonly ILogger<ExportPackageToCanvaCommandHandler> _logger;

    public ExportPackageToCanvaCommandHandler(
        IContentGenerationJobRepository jobRepository,
        IContentGenerationFormatResultRepository formatResultRepository,
        ICanvaUserTokenRepository tokenRepository,
        ICanvaOAuthService oauthService,
        IPptxExportAdapter pptxAdapter,
        ICanvaDesignImportClient importClient,
        ISocialClientRepository clientRepository,
        IGeneratedPackageRepository? packageRepository = null,
        IClock? clock = null,
        ILogger<ExportPackageToCanvaCommandHandler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(jobRepository);
        ArgumentNullException.ThrowIfNull(formatResultRepository);
        ArgumentNullException.ThrowIfNull(tokenRepository);
        ArgumentNullException.ThrowIfNull(oauthService);
        ArgumentNullException.ThrowIfNull(pptxAdapter);
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentNullException.ThrowIfNull(clientRepository);

        _jobRepository = jobRepository;
        _formatResultRepository = formatResultRepository;
        _tokenRepository = tokenRepository;
        _oauthService = oauthService;
        _pptxAdapter = pptxAdapter;
        _importClient = importClient;
        _clientRepository = clientRepository;
        _packageRepository = packageRepository;
        _clock = clock;
        _logger = logger ?? NullLogger<ExportPackageToCanvaCommandHandler>.Instance;
    }

    public async Task<Result<ExportFormatToCanvaResponse>> Handle(
        ExportPackageToCanvaCommand command,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Export package to Canva START: job {JobId}, org {OrgId}, orgIntegration {OrgIntegrationId}.",
            command.JobId, command.OrganizationId, command.OrganizationIntegrationId);

        // 1. Tenant-scoped job lookup
        var job = await _jobRepository.GetByIdAsync(command.JobId, command.OrganizationId, cancellationToken);
        if (job is null)
        {
            _logger.LogWarning("Export package to Canva: job {JobId} not found for org {OrgId}.",
                command.JobId, command.OrganizationId);
            return Result.Failure<ExportFormatToCanvaResponse>(ContentGenerationErrors.JobNotFound);
        }

        // 2. Aggregate the editable composition envelopes across all visual formats of the job,
        //    story first then carousel slides. Each format may carry one (story) or many (carousel).
        var formatResults = await _formatResultRepository.ListByJobAsync(command.JobId, cancellationToken);

        // 3. Check the ORG has a connected Canva account (org-level shared connection).
        var existingToken = await _tokenRepository.GetAsync(
            CanvaOrgConnection.SharedUserId, command.OrganizationIntegrationId, cancellationToken);
        if (existingToken is null)
        {
            _logger.LogWarning(
                "Export package to Canva: no Canva token for orgIntegration {OrgIntegrationId} (not connected).",
                command.OrganizationIntegrationId);
            return Result.Failure<ExportFormatToCanvaResponse>(CanvaExportErrors.NotConnected);
        }

        // 4. Ensure the access token is fresh (rotate if expired). Pass the REAL organizationId.
        var tokenResult = await _oauthService.EnsureFreshAsync(
            CanvaOrgConnection.SharedUserId, command.OrganizationIntegrationId, command.OrganizationId, cancellationToken);
        if (tokenResult.IsFailure)
        {
            return Result.Failure<ExportFormatToCanvaResponse>(tokenResult.Error);
        }

        // 5. Build the multi-page PPTX (offline, deterministic) through the same path used
        // by assemble/download. This keeps Download and Send to Canva aligned.
        var buildResult = await PackagePptxComposer.BuildAsync(
            formatResults,
            _pptxAdapter,
            cancellationToken);
        if (buildResult.IsFailure)
        {
            _logger.LogWarning("Export package to Canva: PPTX build failed for job {JobId}: {Code}.",
                command.JobId, buildResult.Error.Code);
            return Result.Failure<ExportFormatToCanvaResponse>(buildResult.Error);
        }

        _logger.LogInformation("Export package to Canva: built PPTX ({Bytes} bytes) for job {JobId}, importing…",
            buildResult.Value.Length, command.JobId);

        // 6. Title the design after the client (best-effort; falls back to a generic name).
        var designTitle = await ResolveClientNameAsync(job.ClientId, command.OrganizationId, cancellationToken);

        // 7. Import into Canva.
        var importResult = await _importClient.ImportAsync(
            buildResult.Value, tokenResult.Value, designTitle, cancellationToken);

        if (importResult.IsFailure)
        {
            if (string.Equals(importResult.Error.Code, UnauthorizedImportErrorCode, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Export package to Canva: import unauthorized for job {JobId} — revoking token, reconnect required.",
                    command.JobId);
                await _oauthService.RevokeAsync(CanvaOrgConnection.SharedUserId, command.OrganizationIntegrationId, cancellationToken);
                return Result.Failure<ExportFormatToCanvaResponse>(CanvaExportErrors.ReconnectRequired);
            }

            _logger.LogWarning("Export package to Canva: import failed for job {JobId}: {Code}.",
                command.JobId, importResult.Error.Code);
            return Result.Failure<ExportFormatToCanvaResponse>(importResult.Error);
        }

        var canvaResult = importResult.Value;
        if (_packageRepository is not null)
        {
            await _packageRepository.MarkSentToCanvaAsync(
                command.JobId,
                command.OrganizationId,
                command.UserId,
                _clock?.UtcNow ?? DateTime.UtcNow,
                cancellationToken);
        }

        _logger.LogInformation("Export package to Canva OK: job {JobId} → design {DesignId}.",
            command.JobId, canvaResult.DesignId);
        return Result.Success(new ExportFormatToCanvaResponse(
            DesignId: canvaResult.DesignId,
            EditUrl: canvaResult.EditUrl,
            ExpiresAt: canvaResult.ExpiresAt));
    }

    private async Task<string?> ResolveClientNameAsync(Guid? clientId, Guid organizationId, CancellationToken ct)
    {
        if (clientId is null || clientId == Guid.Empty)
        {
            return null;
        }

        var client = await _clientRepository.GetByIdAsync(new ClientId(clientId.Value), organizationId, ct);
        return string.IsNullOrWhiteSpace(client?.Name) ? null : client!.Name;
    }
}
