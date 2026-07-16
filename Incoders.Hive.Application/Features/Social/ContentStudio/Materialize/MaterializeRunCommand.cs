using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Materialize;

/// <summary>
/// Lazily materializes (idempotently) the ContentStudio set for a completed generation run.
/// SourceRun = the content-generation job. Reads the job's per-format results read-only and never
/// mutates any generation row (CS-04, CS-10, CG-READ-02).
/// </summary>
public sealed record MaterializeRunCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid JobId) : IRequest<Result<ContentSetResponse>>;

public sealed class MaterializeRunCommandHandler
    : IRequestHandler<MaterializeRunCommand, Result<ContentSetResponse>>
{
    private readonly IContentSetRepository _sets;
    private readonly IContentItemRepository _items;
    private readonly IContentGenerationFormatResultRepository _formatResults;
    private readonly IContentGenerationFormatAssetRepository _formatAssets;
    private readonly IGeneratedPackageRepository _packages;
    private readonly IClock _clock;

    public MaterializeRunCommandHandler(
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

    public async Task<Result<ContentSetResponse>> Handle(MaterializeRunCommand request, CancellationToken ct)
    {
        // Idempotent: if already materialized for this run, return the existing set + summary.
        var existing = await _sets.GetByRunAsync(request.OrganizationId, request.JobId, ct);
        if (existing is not null)
        {
            var existingSummary = await _sets.GetReviewSummaryAsync(request.OrganizationId, existing.Id, ct);
            return Result.Success(ContentSetResponse.FromDomain(existing, existingSummary));
        }

        var results = await _formatResults.ListByJobAsync(request.JobId, ct);
        var materializable = results.Where(IsMaterializable).ToList();
        if (materializable.Count == 0)
        {
            return Result.Failure<ContentSetResponse>(
                ContentStudioErrors.Validation("The generation run produced no successful formats to materialize."));
        }

        var package = await _packages.GetByJobIdAsync(request.JobId, request.OrganizationId, ct);

        var assetsByResult = (await _formatAssets.ListByJobAsync(request.JobId, ct))
            .GroupBy(a => a.FormatResultId)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.AssetIndex).ToList());

        var now = _clock.UtcNow;
        var items = new List<ContentItem>(materializable.Count);

        foreach (var result in materializable)
        {
            var medium = ContentFormatCatalog.MediumFor(result.FormatKey);
            var assets = assetsByResult.TryGetValue(result.Id, out var rows)
                ? rows.Select(r => new ContentAssetRef("rendered", r.StoragePath, r.MimeType)).ToList()
                : new List<ContentAssetRef>();

            // Phase 1: textual payload seeded from the structured output; visual markup carried for later edit.
            // Text formats keep their raw structured output as the editable body. Visual posts (carousel/
            // story/feed-post) carry a ready-to-publish caption — publicationDescription + hashtags — that
            // the AI produced IN THE SAME generation call, so the studio post is not left without copy.
            var draftText = medium == ContentMedium.Text
                ? result.OutputJson
                : VisualPublicationCaption.Extract(result.OutputJson);

            var draft = new ContentDraft(
                draftText,
                result.CompositionSpecJson,
                assets,
                ContentFormatCatalog.ChannelFor(result.FormatKey));

            var origin = new ContentOrigin(
                GeneratorKind.SingleAnalyzer, request.JobId, package?.Id, result.Id, result.FormatKey);

            var itemResult = ContentItem.Materialize(
                ContentItemId.New(),
                request.OrganizationId,
                request.ClientId,
                origin,
                new ContentKind(result.FormatKey, medium),
                draft,
                now);

            if (itemResult.IsFailure)
            {
                return Result.Failure<ContentSetResponse>(itemResult.Error);
            }

            items.Add(itemResult.Value);
        }

        var setResult = ContentSet.CreateForRun(
            ContentSetId.New(),
            request.OrganizationId,
            request.ClientId,
            SetOrigin.ForRun(GeneratorKind.SingleAnalyzer, request.JobId),
            now);

        if (setResult.IsFailure)
        {
            return Result.Failure<ContentSetResponse>(setResult.Error);
        }

        var set = setResult.Value;
        for (var i = 0; i < items.Count; i++)
        {
            set.AddMember(items[i].Id, i);
        }

        await _items.AddRangeAsync(items, ct);
        await _sets.AddAsync(set, ct);

        var summary = ReviewSummary.From(items.Select(i => i.State));
        return Result.Success(ContentSetResponse.FromDomain(set, summary));
    }

    private static bool IsMaterializable(ContentGenerationFormatResult r) =>
        string.IsNullOrEmpty(r.ErrorCode)
        && (string.Equals(r.Status, "Succeeded", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(r.RenderedAssetUri)
            || !string.IsNullOrEmpty(r.CompositionSpecJson));
}
