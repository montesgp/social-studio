using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Regenerate;

// ─── Request ────────────────────────────────────────────────────────────────

/// <summary>
/// Physically deletes ONE asset (a single file / carousel slide) from storage and
/// removes its DB row. When the deletion empties the owning format result, the result
/// row is removed too so the folder disappears from the package (cascade).
/// Cross-tenant → NotFound. Idempotent: a missing asset returns success.
/// </summary>
public sealed record DeleteAssetCommand(
    Guid AnalysisId,
    Guid OrganizationId,
    Guid ClientId,
    string FormatKey,
    int AssetIndex) : IRequest<Result>;

// ─── Handler ────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="DeleteAssetCommand"/>.
/// <para>
/// Algorithm:
/// 1. Tenant-scope: verify the analysis belongs to <c>OrganizationId</c>.
/// 2. Load the client's asset rows; select the latest format-result version for the format.
/// 3. Find the asset row at <c>AssetIndex</c> within that version.
/// 4. Delete the storage object and the asset row.
/// 5. If the format result now has no assets, delete the format-result row (folder cascade).
/// </para>
/// </summary>
public sealed class DeleteAssetCommandHandler
    : IRequestHandler<DeleteAssetCommand, Result>
{
    private readonly IGeneratedAssetStorage _assetStorage;
    private readonly IContentGenerationFormatAssetRepository _assetRepository;
    private readonly IContentGenerationFormatResultRepository _resultRepository;
    private readonly ISingleContentAnalysisRepository _analysisRepository;

    public DeleteAssetCommandHandler(
        IGeneratedAssetStorage assetStorage,
        IContentGenerationFormatAssetRepository assetRepository,
        IContentGenerationFormatResultRepository resultRepository,
        ISingleContentAnalysisRepository analysisRepository)
    {
        ArgumentNullException.ThrowIfNull(assetStorage);
        ArgumentNullException.ThrowIfNull(assetRepository);
        ArgumentNullException.ThrowIfNull(resultRepository);
        ArgumentNullException.ThrowIfNull(analysisRepository);

        _assetStorage = assetStorage;
        _assetRepository = assetRepository;
        _resultRepository = resultRepository;
        _analysisRepository = analysisRepository;
    }

    public async Task<Result> Handle(DeleteAssetCommand command, CancellationToken cancellationToken)
    {
        // 1. Tenant-scope check
        var analysis = await _analysisRepository.GetByIdAsync(
            command.AnalysisId, command.OrganizationId, cancellationToken);

        if (analysis is null)
        {
            return Result.Failure(ContentGenerationErrors.AnalysisNotFound);
        }

        // 2. Load the client's rows for this format
        var clientPrefix = $"{command.OrganizationId}/{command.ClientId}/{command.AnalysisId}/";
        var allRows = await _assetRepository.ListByAnalysisClientAsync(
            command.OrganizationId, command.AnalysisId, cancellationToken);

        var formatRows = allRows
            .Where(r =>
                r.StoragePath.StartsWith(clientPrefix, StringComparison.OrdinalIgnoreCase)
                && DeriveFormatKeyFromPath(r.StoragePath, clientPrefix) == command.FormatKey)
            .ToList();

        if (formatRows.Count == 0)
        {
            return Result.Success(); // idempotent — nothing stored for this format
        }

        // Operate on the latest format-result version (matches what the package tree shows).
        var latestResultId = formatRows
            .GroupBy(r => r.FormatResultId)
            .OrderByDescending(g => g.Max(r => r.JobCreatedAtUtc))
            .ThenByDescending(g => g.Max(r => r.UpdatedAt))
            .ThenByDescending(g => g.Key)
            .First()
            .Key;

        // 3. Find the asset at the requested index within that version
        var target = formatRows
            .Where(r => r.FormatResultId == latestResultId && r.AssetIndex == command.AssetIndex)
            .ToList();

        if (target.Count == 0)
        {
            return Result.Success(); // idempotent — slide already gone
        }

        // 4. Delete storage object(s) + asset row(s)
        foreach (var asset in target)
        {
            await _assetStorage.DeleteAsync(asset.StoragePath, cancellationToken);
            await _assetRepository.DeleteByIdAsync(asset.Id, cancellationToken);
        }

        // 5. Folder cascade: if the format result is now empty, drop it. Otherwise tombstone the
        //    deleted slide in the composition spec so PPTX/Canva exports skip it WITHOUT renumbering
        //    the remaining slides (their indices/filenames stay stable; the gap is left as-is).
        var remaining = await _assetRepository.CountByFormatResultIdAsync(latestResultId, cancellationToken);
        if (remaining == 0)
        {
            await _resultRepository.DeleteByIdAsync(latestResultId, cancellationToken);
        }
        else
        {
            await TombstoneSlideInSpecAsync(latestResultId, command.AssetIndex, cancellationToken);
        }

        return Result.Success();
    }

    /// <summary>
    /// Marks the deleted carousel slide's envelope as <see cref="CompositionSpecEnvelope.Deleted"/>
    /// in the persisted composition spec, keeping every envelope in place so positional slide
    /// indices remain stable. No-op when the spec is absent or the index is out of range.
    /// </summary>
    private async Task TombstoneSlideInSpecAsync(Guid resultId, int assetIndex, CancellationToken ct)
    {
        var results = await _resultRepository.ListByIdsAsync(new HashSet<Guid> { resultId }, ct);
        var result = results.FirstOrDefault();
        if (result is null)
        {
            return;
        }

        var envelopes = CompositionSpecJson.ParseList(result.CompositionSpecJson).ToList();

        // Carousel slide indices are 1-based; the envelope list is 0-based in slide order.
        var position = assetIndex - 1;
        if (position < 0 || position >= envelopes.Count || envelopes[position].Deleted)
        {
            return;
        }

        envelopes[position] = envelopes[position] with { Deleted = true };
        await _resultRepository.UpdateCompositionSpecAsync(
            resultId, CompositionSpecJson.Serialize(envelopes), ct);
    }

    /// <summary>
    /// Derives the format key from the full storage path given the client-specific prefix.
    /// Mirrors <see cref="DeleteFormatAssetCommandHandler"/>:
    /// "…/story-single.png" → "story-single"; "…/feed-carousel/slide_1.png" → "feed-carousel".
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
