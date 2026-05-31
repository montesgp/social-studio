using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Regenerate;

// ─── Request / Response ───────────────────────────────────────────────────────

/// <summary>
/// Re-renders a single format asset for the given analysis/client and overwrites the
/// existing storage object(s) + DB rows. All other formats are untouched.
/// Cross-tenant or unknown format → NotFound.
/// </summary>
public sealed record RegenerateFormatAssetCommand(
    Guid AnalysisId,
    Guid OrganizationId,
    Guid ClientId,
    string FormatKey,
    string? Objection) : IRequest<Result<RegenerateFormatAssetResponse>>;

/// <summary>Metadata about the regenerated asset(s) returned to the caller.</summary>
public sealed record RegenerateFormatAssetResponse(
    string FormatKey,
    IReadOnlyList<RegeneratedAssetInfo> Assets);

/// <summary>One regenerated asset entry (path + mime + size).</summary>
public sealed record RegeneratedAssetInfo(
    string StoragePath,
    string Filename,
    string Mime,
    long SizeBytes);

// ─── Handler ──────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="RegenerateFormatAssetCommand"/>.
/// <para>
/// Algorithm:
/// 1. Tenant-scope: verify the analysis belongs to <c>OrganizationId</c>.
/// 2. Find the most-recent completed job for (analysisId, orgId, clientId).
/// 3. Load the format result's OutputJson for <c>FormatKey</c> from that job.
/// 4. Re-compose + re-render via the existing visual composition service / renderer.
/// 5. PUT new bytes via <see cref="IGeneratedAssetStorage"/> (x-upsert overwrites).
/// 6. For carousel: delete stale higher-index slide objects that are no longer present.
/// 7. Replace DB child rows via <see cref="IContentGenerationFormatAssetRepository.UpsertManyForFormatAsync"/>.
/// 8. Return the updated asset metadata.
/// </para>
/// </summary>
public sealed class RegenerateFormatAssetCommandHandler
    : IRequestHandler<RegenerateFormatAssetCommand, Result<RegenerateFormatAssetResponse>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IGeneratedAssetStorage _assetStorage;
    private readonly IContentGenerationFormatAssetRepository _assetRepository;
    private readonly IContentGenerationFormatResultRepository _formatResultRepository;
    private readonly IContentGenerationJobRepository _jobRepository;
    private readonly ISingleContentAnalysisRepository _analysisRepository;
    private readonly IVisualCompositionService _composer;
    private readonly IContentRenderer _renderer;
    private readonly IClientBrandSnapshotLoader _brandLoader;

    public RegenerateFormatAssetCommandHandler(
        IGeneratedAssetStorage assetStorage,
        IContentGenerationFormatAssetRepository assetRepository,
        IContentGenerationFormatResultRepository formatResultRepository,
        IContentGenerationJobRepository jobRepository,
        ISingleContentAnalysisRepository analysisRepository,
        IVisualCompositionService composer,
        IContentRenderer renderer,
        IClientBrandSnapshotLoader brandLoader)
    {
        ArgumentNullException.ThrowIfNull(assetStorage);
        ArgumentNullException.ThrowIfNull(assetRepository);
        ArgumentNullException.ThrowIfNull(formatResultRepository);
        ArgumentNullException.ThrowIfNull(jobRepository);
        ArgumentNullException.ThrowIfNull(analysisRepository);
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(brandLoader);

        _assetStorage = assetStorage;
        _assetRepository = assetRepository;
        _formatResultRepository = formatResultRepository;
        _jobRepository = jobRepository;
        _analysisRepository = analysisRepository;
        _composer = composer;
        _renderer = renderer;
        _brandLoader = brandLoader;
    }

    public async Task<Result<RegenerateFormatAssetResponse>> Handle(
        RegenerateFormatAssetCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Tenant-scope check
        var analysis = await _analysisRepository.GetByIdAsync(
            command.AnalysisId, command.OrganizationId, cancellationToken);

        if (analysis is null)
        {
            return Result.Failure<RegenerateFormatAssetResponse>(
                ContentGenerationErrors.AnalysisNotFound);
        }

        // 2. Find the most-recent completed job for this analysis + client
        var job = await _jobRepository.GetLatestByAnalysisAndClientAsync(
            command.AnalysisId, command.OrganizationId, command.ClientId, cancellationToken);

        if (job is null)
        {
            return Result.Failure<RegenerateFormatAssetResponse>(
                ContentGenerationErrors.JobNotFound);
        }

        // 3. Load format results for this job and find the one for our format key
        var formatResults = await _formatResultRepository.ListByJobAsync(job.Id, cancellationToken);
        var formatResult = formatResults.FirstOrDefault(r =>
            string.Equals(r.FormatKey, command.FormatKey, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(r.OutputJson));

        if (formatResult is null)
        {
            return Result.Failure<RegenerateFormatAssetResponse>(
                Error.NotFound(
                    "content-gen.format-not-found",
                    $"No stored result found for format '{command.FormatKey}'."));
        }

        // Deserialize the stored AI payload so the composer can re-use it
        JsonElement payload;
        try
        {
            using var doc = JsonDocument.Parse(formatResult.OutputJson!);
            payload = doc.RootElement.Clone();
        }
        catch
        {
            return Result.Failure<RegenerateFormatAssetResponse>(
                Error.Failure("content-gen.output-json-invalid", "The stored AI output could not be parsed."));
        }

        // 4. Load brand + compose all specs for this format
        var brand = await _brandLoader.LoadAsync(command.OrganizationId, command.ClientId, cancellationToken);
        var specs = await _composer.ComposeManyAsync(command.FormatKey, payload, brand, cancellationToken);

        // Determine the existing asset rows for this format result (for carousel stale-slide cleanup)
        var allRows = await _assetRepository.ListByAnalysisClientAsync(
            command.OrganizationId, command.AnalysisId, cancellationToken);

        var existingFormatRows = allRows
            .Where(r => r.FormatResultId == formatResult.Id)
            .OrderBy(r => r.AssetIndex)
            .ToList();

        // 5. Render each spec and PUT to storage (x-upsert overwrites existing bytes)
        var newAssetRows = new List<FormatAssetRow>();
        var newAssetInfos = new List<RegeneratedAssetInfo>();

        for (var specIndex = 0; specIndex < specs.Count; specIndex++)
        {
            var spec = specs[specIndex];
            var rendered = await _renderer.RenderAsync(spec, new RenderOptions(), cancellationToken);

            if (!rendered.IsSuccess)
            {
                return Result.Failure<RegenerateFormatAssetResponse>(
                    Error.Failure("content-gen.render-failed", rendered.Error.MessageKey));
            }

            var asset = rendered.Value;

            // Build relative path: carousel → carousel/slide_N.png (1-based); else asset.Filename
            string relativePath;
            if (string.Equals(command.FormatKey, "carousel", StringComparison.OrdinalIgnoreCase) && specs.Count > 1)
            {
                relativePath = $"carousel/slide_{specIndex + 1}.png";
            }
            else
            {
                relativePath = asset.Filename;
            }

            var storageKey = new StoredAssetKey(
                command.OrganizationId,
                command.ClientId,
                command.AnalysisId,
                relativePath);

            var storedRef = await _assetStorage.PutAssetAsync(
                storageKey,
                new StoredAssetContent(asset.GetBytes(), asset.Mime),
                cancellationToken);

            var filename = relativePath.Contains('/') ? relativePath.Split('/').Last() : relativePath;
            newAssetRows.Add(new FormatAssetRow(
                Id: Guid.NewGuid(),
                FormatResultId: formatResult.Id,
                AssetIndex: specs.Count > 1 ? specIndex + 1 : 0,
                StoragePath: storedRef.StoragePath,
                Filename: filename,
                MimeType: asset.Mime,
                SizeBytes: storedRef.SizeBytes,
                ContentHash: storedRef.ContentHash,
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: DateTime.UtcNow));

            newAssetInfos.Add(new RegeneratedAssetInfo(
                storedRef.StoragePath,
                filename,
                asset.Mime,
                storedRef.SizeBytes));
        }

        // 6. For carousel: delete stale slide objects that are no longer present
        //    (i.e., the old render had more slides than the new one).
        if (string.Equals(command.FormatKey, "carousel", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var staleRow in existingFormatRows.Skip(specs.Count))
            {
                // Delete the stale storage object
                await _assetStorage.DeleteAsync(staleRow.StoragePath, cancellationToken);
            }
        }

        // 7. Replace DB child rows (delete-then-insert via upsert)
        await _assetRepository.UpsertManyForFormatAsync(formatResult.Id, newAssetRows, cancellationToken);

        return Result.Success(new RegenerateFormatAssetResponse(command.FormatKey, newAssetInfos));
    }
}
