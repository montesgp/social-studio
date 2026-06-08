using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Export;

/// <summary>
/// Command to export the whole VISUAL package of a content-generation job to Canva as a single
/// editable multi-page design (one "Send to Canva" for the package). The flyer and every carousel
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

    // Visual formats, in slide order. Text formats have no composition spec and are skipped.
    private static readonly string[] VisualFormatOrder = ["flyer", "carousel"];

    private readonly IContentGenerationJobRepository _jobRepository;
    private readonly IContentGenerationFormatResultRepository _formatResultRepository;
    private readonly ICanvaUserTokenRepository _tokenRepository;
    private readonly ICanvaOAuthService _oauthService;
    private readonly IPptxExportAdapter _pptxAdapter;
    private readonly ICanvaDesignImportClient _importClient;
    private readonly ISocialClientRepository _clientRepository;

    public ExportPackageToCanvaCommandHandler(
        IContentGenerationJobRepository jobRepository,
        IContentGenerationFormatResultRepository formatResultRepository,
        ICanvaUserTokenRepository tokenRepository,
        ICanvaOAuthService oauthService,
        IPptxExportAdapter pptxAdapter,
        ICanvaDesignImportClient importClient,
        ISocialClientRepository clientRepository)
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
    }

    public async Task<Result<ExportFormatToCanvaResponse>> Handle(
        ExportPackageToCanvaCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Tenant-scoped job lookup
        var job = await _jobRepository.GetByIdAsync(command.JobId, command.OrganizationId, cancellationToken);
        if (job is null)
        {
            return Result.Failure<ExportFormatToCanvaResponse>(ContentGenerationErrors.JobNotFound);
        }

        // 2. Aggregate the editable composition envelopes across all visual formats of the job,
        //    flyer first then carousel slides. Each format may carry one (flyer) or many (carousel).
        var formatResults = await _formatResultRepository.ListByJobAsync(command.JobId, cancellationToken);
        var byFormat = formatResults
            .GroupBy(r => r.FormatKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var envelopes = new List<CompositionSpecEnvelope>();
        foreach (var formatKey in VisualFormatOrder)
        {
            if (byFormat.TryGetValue(formatKey, out var result))
            {
                envelopes.AddRange(CompositionSpecJson.ParseList(result.CompositionSpecJson));
            }
        }

        if (envelopes.Count == 0)
        {
            // No editable visual composition in this job (e.g. only a text format).
            return Result.Failure<ExportFormatToCanvaResponse>(ContentGenerationErrors.NotExportable);
        }

        // 3. Check the ORG has a connected Canva account (org-level shared connection).
        var existingToken = await _tokenRepository.GetAsync(
            CanvaOrgConnection.SharedUserId, command.OrganizationIntegrationId, cancellationToken);
        if (existingToken is null)
        {
            return Result.Failure<ExportFormatToCanvaResponse>(CanvaExportErrors.NotConnected);
        }

        // 4. Ensure the access token is fresh (rotate if expired). Pass the REAL organizationId.
        var tokenResult = await _oauthService.EnsureFreshAsync(
            CanvaOrgConnection.SharedUserId, command.OrganizationIntegrationId, command.OrganizationId, cancellationToken);
        if (tokenResult.IsFailure)
        {
            return Result.Failure<ExportFormatToCanvaResponse>(tokenResult.Error);
        }

        // 5. Build the multi-page PPTX (offline, deterministic) — one slide per envelope.
        var buildResult = await _pptxAdapter.BuildManyAsync(envelopes, cancellationToken);
        if (buildResult.IsFailure)
        {
            return Result.Failure<ExportFormatToCanvaResponse>(buildResult.Error);
        }

        // 6. Title the design after the client (best-effort; falls back to a generic name).
        var designTitle = await ResolveClientNameAsync(job.ClientId, command.OrganizationId, cancellationToken);

        // 7. Import into Canva.
        var importResult = await _importClient.ImportAsync(
            buildResult.Value, tokenResult.Value, designTitle, cancellationToken);

        if (importResult.IsFailure)
        {
            if (string.Equals(importResult.Error.Code, UnauthorizedImportErrorCode, StringComparison.OrdinalIgnoreCase))
            {
                await _oauthService.RevokeAsync(CanvaOrgConnection.SharedUserId, command.OrganizationIntegrationId, cancellationToken);
                return Result.Failure<ExportFormatToCanvaResponse>(CanvaExportErrors.ReconnectRequired);
            }

            return Result.Failure<ExportFormatToCanvaResponse>(importResult.Error);
        }

        var canvaResult = importResult.Value;
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
