using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Materialize;

/// <summary>
/// Materializes (idempotently) the ContentStudio set for ALL formats a client generated from a single
/// video analysis, aggregating every generation job. The studio is analysis+client scoped: the set's
/// source-run key is the analysis id, so re-opening surfaces newly generated formats too (incremental).
/// Reads generation rows read-only and never mutates them (CS-04, CS-10, CG-READ-02).
/// </summary>
public sealed record MaterializeAnalysisCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid AnalysisId) : IRequest<Result<ContentSetResponse>>;

public sealed class MaterializeAnalysisCommandHandler
    : IRequestHandler<MaterializeAnalysisCommand, Result<ContentSetResponse>>
{
    private readonly IContentSetRepository _sets;
    private readonly IContentItemRepository _items;
    private readonly IContentGenerationFormatResultRepository _formatResults;
    private readonly IContentGenerationFormatAssetRepository _formatAssets;
    private readonly IGeneratedPackageRepository _packages;
    private readonly IClock _clock;

    public MaterializeAnalysisCommandHandler(
        IContentSetRepository sets,
        IContentItemRepository items,
        IContentGenerationFormatResultRepository formatResults,
        IContentGenerationFormatAssetRepository formatAssets,
        IGeneratedPackageRepository packages,
        IClock clock)
    {
        _sets = sets;
        _items = items;
        _formatResults = formatResults;
        _formatAssets = formatAssets;
        _packages = packages;
        _clock = clock;
    }

    public async Task<Result<ContentSetResponse>> Handle(MaterializeAnalysisCommand request, CancellationToken ct)
    {
        // One studio set per (analysis, client). The set table's idempotency unique index is on
        // (org, source_run_id) only, so we fold both ids into a synthetic run id; otherwise several
        // clients sharing one analysis would co-mingle into the first client's set.
        var setRunId = DeriveSetRunId(request.AnalysisId, request.ClientId);
        var existing = await _sets.GetByRunAsync(request.OrganizationId, setRunId, ct);

        var results = await _formatResults.ListByAnalysisAndClientAsync(
            request.OrganizationId, request.AnalysisId, request.ClientId, ct);

        // Latest materializable result per format key — a regenerated format supersedes the older job's.
        var latestByFormat = results
            .Where(IsMaterializable)
            .GroupBy(r => r.FormatKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(r => r.UpdatedAt).ThenByDescending(r => r.CreatedAt).First())
            .ToList();

        if (latestByFormat.Count == 0)
        {
            if (existing is not null)
            {
                var emptySummary = await _sets.GetReviewSummaryAsync(request.OrganizationId, existing.Id, ct);
                return Result.Success(ContentSetResponse.FromDomain(existing, emptySummary));
            }

            return Result.Failure<ContentSetResponse>(
                ContentStudioErrors.Validation("This analysis produced no successful formats to materialize."));
        }

        var assetsByResult = (await _formatAssets.ListByAnalysisAndClientAsync(
                request.OrganizationId, request.AnalysisId, request.ClientId, ct))
            .GroupBy(a => a.FormatResultId)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.AssetIndex).ToList());

        var now = _clock.UtcNow;

        if (existing is null)
        {
            var builtItems = new List<ContentItem>(latestByFormat.Count);
            foreach (var result in latestByFormat)
            {
                var built = await BuildItemAsync(request, result, assetsByResult, now, ct);
                if (built.IsFailure)
                {
                    return Result.Failure<ContentSetResponse>(built.Error);
                }

                builtItems.Add(built.Value);
            }

            var setResult = ContentSet.CreateForRun(
                ContentSetId.New(),
                request.OrganizationId,
                request.ClientId,
                SetOrigin.ForRun(GeneratorKind.SingleAnalyzer, setRunId),
                now);

            if (setResult.IsFailure)
            {
                return Result.Failure<ContentSetResponse>(setResult.Error);
            }

            var set = setResult.Value;
            for (var i = 0; i < builtItems.Count; i++)
            {
                set.AddMember(builtItems[i].Id, i);
            }

            await _items.AddRangeAsync(builtItems, ct);
            await _sets.AddAsync(set, ct);

            var summary = ReviewSummary.From(builtItems.Select(i => i.State));
            return Result.Success(ContentSetResponse.FromDomain(set, summary));
        }

        // Incremental: add items for any newly generated formats not already in the set, preserving
        // existing (possibly already-edited) items.
        var existingItems = await _items.ListBySetAsync(request.OrganizationId, existing.Id, ct);
        var existingFormatKeys = existingItems
            .Select(i => i.Kind.FormatKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = latestByFormat
            .Where(r => !existingFormatKeys.Contains(r.FormatKey))
            .ToList();

        if (toAdd.Count > 0)
        {
            var addedItems = new List<ContentItem>(toAdd.Count);
            foreach (var result in toAdd)
            {
                var built = await BuildItemAsync(request, result, assetsByResult, now, ct);
                if (built.IsFailure)
                {
                    return Result.Failure<ContentSetResponse>(built.Error);
                }

                addedItems.Add(built.Value);
            }

            var nextOrder = existingItems.Count;
            for (var i = 0; i < addedItems.Count; i++)
            {
                existing.AddMember(addedItems[i].Id, nextOrder + i);
            }

            await _items.AddRangeAsync(addedItems, ct);
            await _sets.UpdateAsync(existing, ct);
        }

        var finalSummary = await _sets.GetReviewSummaryAsync(request.OrganizationId, existing.Id, ct);
        return Result.Success(ContentSetResponse.FromDomain(existing, finalSummary));
    }

    private async Task<Result<ContentItem>> BuildItemAsync(
        MaterializeAnalysisCommand request,
        ContentGenerationFormatResult result,
        Dictionary<Guid, List<FormatAssetRow>> assetsByResult,
        DateTime now,
        CancellationToken ct)
    {
        var medium = ContentFormatCatalog.MediumFor(result.FormatKey);
        var assets = assetsByResult.TryGetValue(result.Id, out var rows)
            ? rows.Select(r => new ContentAssetRef("rendered", r.StoragePath, r.MimeType)).ToList()
            : new List<ContentAssetRef>();

        var draft = new ContentDraft(
            medium == ContentMedium.Text ? result.OutputJson : null,
            result.CompositionSpecJson,
            assets,
            ContentFormatCatalog.ChannelFor(result.FormatKey));

        var package = await _packages.GetByJobIdAsync(result.JobId, request.OrganizationId, ct);

        var origin = new ContentOrigin(
            GeneratorKind.SingleAnalyzer, result.JobId, package?.Id, result.Id, result.FormatKey);

        return ContentItem.Materialize(
            ContentItemId.New(),
            request.OrganizationId,
            request.ClientId,
            origin,
            new ContentKind(result.FormatKey, medium),
            draft,
            now);
    }

    /// <summary>
    /// Deterministic synthetic run id for the (analysis, client) pair, so each client gets an
    /// isolated, idempotent studio set even though they share the same analysis.
    /// </summary>
    private static Guid DeriveSetRunId(Guid analysisId, Guid clientId)
    {
        var a = analysisId.ToByteArray();
        var c = clientId.ToByteArray();
        var combined = new byte[16];
        for (var i = 0; i < combined.Length; i++)
        {
            combined[i] = (byte)(a[i] ^ c[i]);
        }

        return new Guid(combined);
    }

    private static bool IsMaterializable(ContentGenerationFormatResult r) =>
        string.IsNullOrEmpty(r.ErrorCode)
        && (string.Equals(r.Status, "Succeeded", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(r.RenderedAssetUri)
            || !string.IsNullOrEmpty(r.CompositionSpecJson));
}
