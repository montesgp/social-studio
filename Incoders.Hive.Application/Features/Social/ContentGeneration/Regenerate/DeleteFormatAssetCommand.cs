using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Regenerate;

// ─── Request / Response ───────────────────────────────────────────────────────

/// <summary>
/// Physically deletes a single format's asset object(s) from storage and removes
/// the corresponding DB rows. Cross-tenant → NotFound.
/// Idempotent: if no rows exist for the format, returns success without side effects.
/// </summary>
public sealed record DeleteFormatAssetCommand(
    Guid AnalysisId,
    Guid OrganizationId,
    Guid ClientId,
    string FormatKey) : IRequest<Result>;

// ─── Handler ──────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="DeleteFormatAssetCommand"/>.
/// <para>
/// Algorithm:
/// 1. Tenant-scope: verify the analysis belongs to <c>OrganizationId</c>.
/// 2. Load all asset rows for this analysis/org.
/// 3. Filter to rows matching <c>FormatKey</c> (by storage path prefix or format result id lookup).
/// 4. Call <see cref="IGeneratedAssetStorage.DeleteAsync"/> for each storage object.
/// 5. Call <see cref="IContentGenerationFormatAssetRepository.DeleteByFormatResultIdAsync"/>
///    to remove the DB rows.
/// </para>
/// </summary>
public sealed class DeleteFormatAssetCommandHandler
    : IRequestHandler<DeleteFormatAssetCommand, Result>
{
    private readonly IGeneratedAssetStorage _assetStorage;
    private readonly IContentGenerationFormatAssetRepository _assetRepository;
    private readonly ISingleContentAnalysisRepository _analysisRepository;

    public DeleteFormatAssetCommandHandler(
        IGeneratedAssetStorage assetStorage,
        IContentGenerationFormatAssetRepository assetRepository,
        ISingleContentAnalysisRepository analysisRepository)
    {
        ArgumentNullException.ThrowIfNull(assetStorage);
        ArgumentNullException.ThrowIfNull(assetRepository);
        ArgumentNullException.ThrowIfNull(analysisRepository);

        _assetStorage = assetStorage;
        _assetRepository = assetRepository;
        _analysisRepository = analysisRepository;
    }

    public async Task<Result> Handle(
        DeleteFormatAssetCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Tenant-scope check
        var analysis = await _analysisRepository.GetByIdAsync(
            command.AnalysisId, command.OrganizationId, cancellationToken);

        if (analysis is null)
        {
            return Result.Failure(ContentGenerationErrors.AnalysisNotFound);
        }

        // 2. Load all asset rows for this org/analysis (all clients combined)
        var allRows = await _assetRepository.ListByAnalysisClientAsync(
            command.OrganizationId, command.AnalysisId, cancellationToken);

        // 3. Filter to rows belonging to this format for this specific client.
        //    Path layout: {orgId}/{clientId}/{analysisId}/{relative}
        //    Detect format by the relative segment after the analysis path prefix.
        //    e.g. ".../flyer.png" → format key "flyer"
        //         ".../carousel/slide_1.png" → format key "carousel"
        var clientPrefix = $"{command.OrganizationId}/{command.ClientId}/{command.AnalysisId}/";
        var formatRows = allRows
            .Where(r =>
                r.StoragePath.StartsWith(clientPrefix, StringComparison.OrdinalIgnoreCase)
                && DeriveFormatKeyFromPath(r.StoragePath, clientPrefix) == command.FormatKey)
            .ToList();

        // Idempotent: nothing to do when no rows found
        if (formatRows.Count == 0)
        {
            return Result.Success();
        }

        // 4. Delete each storage object
        foreach (var row in formatRows)
        {
            await _assetStorage.DeleteAsync(row.StoragePath, cancellationToken);
        }

        // 5. Remove DB rows per unique format result id
        var formatResultIds = formatRows.Select(r => r.FormatResultId).Distinct().ToList();
        foreach (var formatResultId in formatResultIds)
        {
            await _assetRepository.DeleteByFormatResultIdAsync(formatResultId, cancellationToken);
        }

        return Result.Success();
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Derives the format key from the full storage path given the client-specific prefix.
    /// <para>Examples (clientPrefix = "{org}/{client}/{analysis}/"):
    /// <list type="bullet">
    ///   <item>"…/flyer.png" → "flyer"</item>
    ///   <item>"…/reel-script.txt" → "reel-script"</item>
    ///   <item>"…/carousel/slide_1.png" → "carousel"</item>
    /// </list>
    /// </para>
    /// </summary>
    private static string DeriveFormatKeyFromPath(string storagePath, string clientPrefix)
    {
        var relative = storagePath[clientPrefix.Length..];
        var slashIndex = relative.IndexOf('/');
        var firstSegment = slashIndex >= 0 ? relative[..slashIndex] : relative;
        var dotIndex = firstSegment.LastIndexOf('.');
        return dotIndex > 0 ? firstSegment[..dotIndex] : firstSegment;
    }
}
