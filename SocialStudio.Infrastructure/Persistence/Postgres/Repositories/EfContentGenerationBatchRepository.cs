using System.Text.Json;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// EF adapter for <see cref="IContentGenerationBatchRepository"/>.
///
/// Progress is COUNTED from the child jobs on every read rather than stored. The counting query is
/// backed by ix_content_generation_jobs_batch_id, and it cannot go stale the way a worker-maintained
/// counter would when the process dies mid-piece.
/// </summary>
public sealed class EfContentGenerationBatchRepository : IContentGenerationBatchRepository
{
    /// <summary>
    /// Terminal job statuses that produced usable output. "Partial" counts as done because the
    /// pipeline uses it for a job that safeguarded real work before failing a sub-step — the piece
    /// exists in the bank and the operator can judge it.
    /// </summary>
    private static readonly string[] SucceededStatuses = ["Completed", "Partial"];

    private static readonly string[] FailedStatuses = ["Failed"];

    /// <summary>Batch statuses that mean "still working", for the global progress surface.</summary>
    private static readonly string[] ActiveBatchStatuses = ["Queued", "Running"];

    private readonly AppIdentityDbContext _db;

    public EfContentGenerationBatchRepository(AppIdentityDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task InsertAsync(ContentGenerationBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);

        _db.Set<ContentGenerationBatchData>().Add(new ContentGenerationBatchData
        {
            Id = batch.Id,
            OrganizationId = batch.OrganizationId,
            ClientId = batch.ClientId,
            AnalysisId = batch.AnalysisId,
            Status = batch.Status,
            CompositionJson = JsonSerializer.Serialize(batch.Composition),
            TotalPieces = batch.TotalPieces,
            CorrelationId = batch.CorrelationId,
            CreatedAt = batch.CreatedAt,
            UpdatedAt = batch.UpdatedAt,
            CompletedAt = batch.CompletedAt
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<ContentGenerationBatch?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct)
    {
        var data = await _db.Set<ContentGenerationBatchData>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct);

        return data is null ? null : Map(data);
    }

    public async Task<ContentGenerationBatchProgress?> GetProgressAsync(
        Guid id,
        Guid organizationId,
        CancellationToken ct)
    {
        var batch = await _db.Set<ContentGenerationBatchData>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct);

        if (batch is null)
        {
            return null;
        }

        // Filtering on BatchId alone is safe here only because the batch row above was already
        // tenant-checked, and a job's batch is set once at creation.
        var pieces = await _db.Set<ContentGenerationJobData>()
            .AsNoTracking()
            .Where(x => x.BatchId == id)
            .Select(x => new { x.Status, x.BatchPieceIndex, x.RequestedFormats })
            .ToListAsync(ct);

        var completed = pieces.Count(p => SucceededStatuses.Contains(p.Status));
        var failed = pieces.Count(p => FailedStatuses.Contains(p.Status));
        var terminal = completed + failed;

        // Anything not terminal is either actively running or still waiting its turn. Treating
        // "Queued" as the only waiting status would silently drop intermediate pipeline statuses
        // such as AnalyzingProfile and under-report the batch.
        var running = pieces.Count(p =>
            !SucceededStatuses.Contains(p.Status)
            && !FailedStatuses.Contains(p.Status)
            && !string.Equals(p.Status, "Queued", StringComparison.OrdinalIgnoreCase));

        var queued = pieces.Count - terminal - running;

        var current = pieces
            .Where(p =>
                !SucceededStatuses.Contains(p.Status)
                && !FailedStatuses.Contains(p.Status)
                && !string.Equals(p.Status, "Queued", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.BatchPieceIndex ?? int.MaxValue)
            .FirstOrDefault();

        return new ContentGenerationBatchProgress(
            BatchId: batch.Id,
            Status: batch.Status,
            TotalPieces: batch.TotalPieces,
            CompletedPieces: completed,
            FailedPieces: failed,
            RunningPieces: running,
            QueuedPieces: queued,
            CurrentPieceIndex: current?.BatchPieceIndex,
            CurrentFormatKey: current?.RequestedFormats.FirstOrDefault());
    }

    public async Task<IReadOnlyList<ContentGenerationBatch>> ListActiveAsync(
        Guid organizationId,
        CancellationToken ct)
    {
        var rows = await _db.Set<ContentGenerationBatchData>()
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ActiveBatchStatuses.Contains(x.Status))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(Map).ToList();
    }

    public async Task UpdateStatusAsync(Guid id, string status, DateTime? completedAt, CancellationToken ct)
    {
        var data = await _db.Set<ContentGenerationBatchData>().FirstOrDefaultAsync(x => x.Id == id, ct);

        if (data is null)
        {
            return;
        }

        data.Status = status;
        data.UpdatedAt = DateTime.UtcNow;

        if (completedAt is not null)
        {
            data.CompletedAt = completedAt;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task ReconcileStatusAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await _db.Set<ContentGenerationBatchData>().FirstOrDefaultAsync(x => x.Id == batchId, ct);

        if (batch is null)
        {
            return;
        }

        var statuses = await _db.Set<ContentGenerationJobData>()
            .AsNoTracking()
            .Where(x => x.BatchId == batchId)
            .Select(x => x.Status)
            .ToListAsync(ct);

        var succeeded = statuses.Count(s => SucceededStatuses.Contains(s));
        var failed = statuses.Count(s => FailedStatuses.Contains(s));
        var terminal = succeeded + failed;

        // A batch is only finished once EVERY piece it promised exists AND has reached a terminal
        // state. Comparing against the child count alone would close a batch during the window where
        // its jobs are still being inserted, stranding the pieces written afterwards.
        var allPiecesAccountedFor = statuses.Count >= batch.TotalPieces && terminal >= batch.TotalPieces;

        if (!allPiecesAccountedFor)
        {
            // Anything already dequeued means the batch is under way rather than merely queued.
            var started = statuses.Any(s => !string.Equals(s, "Queued", StringComparison.OrdinalIgnoreCase));
            var nextStatus = started ? "Running" : "Queued";

            if (!string.Equals(batch.Status, nextStatus, StringComparison.Ordinal))
            {
                batch.Status = nextStatus;
                batch.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }

            return;
        }

        var finalStatus = failed == 0
            ? "Completed"
            : succeeded == 0 ? "Failed" : "Partial";

        // Every finishing piece calls this, so completion must be write-once: re-stamping CompletedAt
        // would make the batch look like it finished later each time.
        if (string.Equals(batch.Status, finalStatus, StringComparison.Ordinal) && batch.CompletedAt is not null)
        {
            return;
        }

        batch.Status = finalStatus;
        batch.CompletedAt ??= DateTime.UtcNow;
        batch.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    private static ContentGenerationBatch Map(ContentGenerationBatchData data) =>
        new(
            Id: data.Id,
            OrganizationId: data.OrganizationId,
            ClientId: data.ClientId,
            AnalysisId: data.AnalysisId,
            Status: data.Status,
            Composition: DeserializeComposition(data.CompositionJson),
            TotalPieces: data.TotalPieces,
            CorrelationId: data.CorrelationId,
            CreatedAt: data.CreatedAt,
            UpdatedAt: data.UpdatedAt,
            CompletedAt: data.CompletedAt);

    private static IReadOnlyList<ContentGenerationBatchEntry> DeserializeComposition(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ContentGenerationBatchEntry>>(json) ?? [];
        }
        catch (JsonException)
        {
            // A malformed cart must not take down the progress surface for every other batch.
            return [];
        }
    }
}
