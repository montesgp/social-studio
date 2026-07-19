using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly ILogger<MaterializeAnalysisCommandHandler> _logger;

    public MaterializeAnalysisCommandHandler(
        IContentSetRepository sets,
        IContentItemRepository items,
        IContentGenerationFormatResultRepository formatResults,
        IContentGenerationFormatAssetRepository formatAssets,
        IGeneratedPackageRepository packages,
        IClock clock,
        ILogger<MaterializeAnalysisCommandHandler>? logger = null)
    {
        _sets = sets;
        _items = items;
        _formatResults = formatResults;
        _formatAssets = formatAssets;
        _packages = packages;
        _clock = clock;
        _logger = logger ?? NullLogger<MaterializeAnalysisCommandHandler>.Instance;
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

        // Incremental: add items for newly generated formats, and REFRESH pristine items whose source
        // format was regenerated (newer result). Edited/reviewed items are protected by the aggregate.
        var existingItems = await _items.ListBySetAsync(request.OrganizationId, existing.Id, ct);
        var existingFormatKeys = existingItems
            .Select(i => i.Kind.FormatKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // First item per format key (sets hold one item per format on the single-analyzer surface).
        var itemByFormat = existingItems
            .GroupBy(i => i.Kind.FormatKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Refresh in place: a regenerated result has a newer UpdatedAt than the item's last snapshot.
        var refreshed = new List<ContentItem>();
        foreach (var result in latestByFormat)
        {
            if (!itemByFormat.TryGetValue(result.FormatKey, out var item)
                || result.UpdatedAt <= item.UpdatedAtUtc)
            {
                continue;
            }

            var (_, draft, origin) = await BuildDraftAsync(request, result, assetsByResult, ct);
            if (item.RefreshFromSource(origin, draft, now))
            {
                refreshed.Add(item);
            }
        }

        foreach (var item in refreshed)
        {
            await _items.UpdateAsync(item, ct);
        }

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
        var (medium, draft, origin) = await BuildDraftAsync(request, result, assetsByResult, ct);

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
    /// Builds the draft payload and soft source reference for a format result. Shared by initial
    /// materialization and the regeneration refresh path, so both derive identical drafts.
    /// </summary>
    private async Task<(ContentMedium Medium, ContentDraft Draft, ContentOrigin Origin)> BuildDraftAsync(
        MaterializeAnalysisCommand request,
        ContentGenerationFormatResult result,
        Dictionary<Guid, List<FormatAssetRow>> assetsByResult,
        CancellationToken ct)
    {
        var medium = ContentFormatCatalog.MediumFor(result.FormatKey);
        var assets = assetsByResult.TryGetValue(result.Id, out var rows)
            ? rows.Select(r => new ContentAssetRef("rendered", r.StoragePath, r.MimeType)).ToList()
            : new List<ContentAssetRef>();

        // Visual formats get the same extract-then-deterministic-fallback caption wiring as
        // MaterializeRunCommandHandler, via the shared VisualCaptionResolver — the two materialization
        // paths must never drift on "caption is required, never empty" again.
        Caption? caption = null;
        if (medium != ContentMedium.Text)
        {
            var resolution = VisualCaptionResolver.Resolve(result.OutputJson, result.CompositionSpecJson);
            caption = resolution.Caption;

            if (resolution.UsedFallback)
            {
                _logger.LogWarning(
                    "ContentStudio: no caption produced by generation for format {FormatKey} (analysis {AnalysisId}, result {ResultId}); " +
                    "using a deterministic fallback derived from slide content. FallbackSucceeded={FallbackSucceeded}",
                    result.FormatKey, request.AnalysisId, result.Id, resolution.Caption is not null);
            }
        }

        var draft = new ContentDraft(
            medium == ContentMedium.Text ? result.OutputJson : null,
            caption,
            result.CompositionSpecJson,
            assets,
            ContentFormatCatalog.ChannelFor(result.FormatKey));

        var package = await _packages.GetByJobIdAsync(result.JobId, request.OrganizationId, ct);

        var origin = new ContentOrigin(
            GeneratorKind.SingleAnalyzer, result.JobId, package?.Id, result.Id, result.FormatKey);

        return (medium, draft, origin);
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
