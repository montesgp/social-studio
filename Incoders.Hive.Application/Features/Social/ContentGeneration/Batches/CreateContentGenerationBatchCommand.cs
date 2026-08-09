using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentFormats;
using Incoders.Hive.Domain.Social.ContentGeneration.Batches;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Batches;

/// <summary>One tile the operator dropped in the cart.</summary>
public sealed record BatchCompositionItem(string FormatKey, int Count);

/// <summary>
/// Starts a multi-piece generation batch from a completed analysis.
///
/// <paramref name="AssortedTotal"/> is the "surprise me x N" tile: the system spreads N across the
/// catalog's deliverable formats. It can be combined with explicit tiles — both land in the same
/// cart and the totals add up.
/// </summary>
public sealed record CreateContentGenerationBatchCommand(
    Guid OrganizationId,
    Guid? ClientId,
    Guid AnalysisId,
    IReadOnlyList<BatchCompositionItem> Composition,
    int? AssortedTotal,
    Guid? CorrelationId) : IRequest<Result<CreateContentGenerationBatchResponse>>;

/// <summary>
/// Accepted response (202). <paramref name="Composition"/> is the RESOLVED mix — including whatever
/// the assorted tile decided — so the operator can see what they actually ordered.
/// </summary>
public sealed record CreateContentGenerationBatchResponse(
    Guid BatchId,
    int TotalPieces,
    IReadOnlyList<ContentGenerationBatchEntry> Composition,
    string StatusUrl);

/// <summary>
/// Turns a cart into N queued jobs, one per piece, and stops there.
///
/// It does NOT touch the generation pipeline. Every piece is an ordinary content generation job of
/// exactly one format, so the existing worker, processor and watchdog handle a batch piece without
/// knowing batches exist.
/// </summary>
public sealed class CreateContentGenerationBatchCommandHandler
    : IRequestHandler<CreateContentGenerationBatchCommand, Result<CreateContentGenerationBatchResponse>>
{
    private readonly IBatchAnalysisLookup _analyses;
    private readonly IBatchPieceCapLookup _pieceCaps;
    private readonly IContentGenerationJobRepository _jobs;
    private readonly IContentGenerationBatchRepository _batches;
    private readonly IContentGenerationJobQueue _queue;
    private readonly ContentGenerationBatchOptions _options;

    public CreateContentGenerationBatchCommandHandler(
        IBatchAnalysisLookup analyses,
        IBatchPieceCapLookup pieceCaps,
        IContentGenerationJobRepository jobs,
        IContentGenerationBatchRepository batches,
        IContentGenerationJobQueue queue,
        ContentGenerationBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(analyses);
        ArgumentNullException.ThrowIfNull(pieceCaps);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(options);

        _analyses = analyses;
        _pieceCaps = pieceCaps;
        _jobs = jobs;
        _batches = batches;
        _queue = queue;
        _options = options;
    }

    public async Task<Result<CreateContentGenerationBatchResponse>> Handle(
        CreateContentGenerationBatchCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ClientId is not { } clientId || clientId == Guid.Empty)
        {
            return Result.Failure<CreateContentGenerationBatchResponse>(
                ContentGenerationErrors.ClientRequired);
        }

        // Validate the analysis BEFORE anything is written: a batch that fails halfway would leave
        // orphan jobs already draining through the worker and burning tokens.
        var analysisExists = await _analyses.ExistsAsync(command.AnalysisId, command.OrganizationId, cancellationToken);

        if (!analysisExists)
        {
            return Result.Failure<CreateContentGenerationBatchResponse>(
                ContentGenerationErrors.AnalysisNotFound);
        }

        var clientCap = await _pieceCaps.GetClientPieceCapAsync(clientId, command.OrganizationId, cancellationToken);
        var maxPieces = clientCap ?? _options.DefaultMaxPiecesPerBatch;

        var compositionResult = BuildComposition(command, maxPieces);

        if (compositionResult.IsFailure)
        {
            return Result.Failure<CreateContentGenerationBatchResponse>(compositionResult.Error);
        }

        var composition = compositionResult.Value;
        var pieces = composition.Expand();

        var now = DateTime.UtcNow;
        var batchId = Guid.NewGuid();
        var correlationId = command.CorrelationId ?? Guid.NewGuid();

        var entries = composition.Entries
            .Select(entry => new ContentGenerationBatchEntry(entry.FormatKey, entry.Count))
            .ToList();

        await _batches.InsertAsync(
            new ContentGenerationBatch(
                Id: batchId,
                OrganizationId: command.OrganizationId,
                ClientId: clientId,
                AnalysisId: command.AnalysisId,
                Status: "Queued",
                Composition: entries,
                TotalPieces: composition.TotalPieces,
                CorrelationId: correlationId,
                CreatedAt: now,
                UpdatedAt: now,
                CompletedAt: null),
            cancellationToken);

        var jobIds = new List<Guid>(pieces.Count);

        for (var index = 0; index < pieces.Count; index++)
        {
            var jobId = Guid.NewGuid();
            jobIds.Add(jobId);

            await _jobs.InsertAsync(
                new ContentGenerationJob(
                    Id: jobId,
                    OrganizationId: command.OrganizationId,
                    ClientId: clientId,
                    AnalysisId: command.AnalysisId,
                    // Exactly one format per job. The format result row is keyed (JobId, FormatKey),
                    // so a second piece of the same format in one job would overwrite the first.
                    RequestedFormats: [pieces[index]],
                    Status: "Queued",
                    StatusReason: null,
                    ErrorCode: null,
                    BrandSnapshotJson: "{}",
                    ManifestJson: null,
                    CorrelationId: correlationId,
                    CreatedAt: now,
                    UpdatedAt: now,
                    StartedAt: null,
                    CompletedAt: null,
                    RegenerationObjection: null,
                    BatchId: batchId,
                    BatchPieceIndex: index),
                cancellationToken);
        }

        // Enqueue only AFTER every piece is persisted. Enqueuing as we go would let a fast worker
        // finish piece 0 and read a batch that is still half-written, reporting a total that grows.
        foreach (var jobId in jobIds)
        {
            await _queue.EnqueueAsync(jobId, cancellationToken);
        }

        return Result.Success(new CreateContentGenerationBatchResponse(
            BatchId: batchId,
            TotalPieces: composition.TotalPieces,
            Composition: entries,
            StatusUrl: $"/api/social/content-generation-batches/{batchId}/progress"));
    }

    private static Result<BatchComposition> BuildComposition(
        CreateContentGenerationBatchCommand command,
        int maxPieces)
    {
        var entries = (command.Composition ?? [])
            .Select(item => new CompositionEntry(item.FormatKey, item.Count))
            .ToList();

        if (command.AssortedTotal is { } assortedTotal)
        {
            // The pool is derived from the catalog, never a literal: a new deliverable row joins the
            // assortment automatically.
            var pool = FormatCatalog.All()
                .Where(definition => !definition.IsAuxiliary)
                .Select(definition => definition.FormatKey)
                .ToList();

            var assorted = AssortedCompositionPlanner.Plan(assortedTotal, pool, maxPieces);

            if (assorted.IsFailure)
            {
                return assorted;
            }

            entries.AddRange(assorted.Value.Entries);
        }

        // One Create call validates the merged cart, so an explicit tile plus an assorted tile are
        // capped as a whole rather than each on its own.
        return BatchComposition.Create(entries, maxPieces);
    }
}
