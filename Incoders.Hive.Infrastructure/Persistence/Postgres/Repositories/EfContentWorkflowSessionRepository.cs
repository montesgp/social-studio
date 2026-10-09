using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.Workflow;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Incoders.Hive.Infrastructure.Social.Workflow;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// EF adapter for <see cref="IContentWorkflowSessionRepository"/> (design decision #1: normalized
/// Postgres tables replace <c>InMemoryContentWorkflowService</c>'s <c>ConcurrentDictionary</c>).
/// </summary>
public sealed class EfContentWorkflowSessionRepository : IContentWorkflowSessionRepository
{
    private readonly AppIdentityDbContext _db;

    public EfContentWorkflowSessionRepository(AppIdentityDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task InsertAsync(
        WorkflowSession session,
        Guid organizationId,
        Guid clientId,
        string clientName,
        string status,
        string? configJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var now = DateTime.UtcNow;

        var data = new ContentWorkflowSessionData
        {
            Id = session.Id,
            OrganizationId = organizationId,
            ClientId = clientId,
            ClientName = clientName,
            Status = status,
            ConfigJson = configJson,
            CreatedAt = now,
            UpdatedAt = now,
            Candidates = session.Candidates.Select(candidate => ToCandidateData(session.Id, candidate, now)).ToList(),
        };

        _db.Set<ContentWorkflowSessionData>().Add(data);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkflowSession?> GetByIdAsync(
        Guid id, Guid organizationId, CancellationToken cancellationToken = default)
    {
        var data = await _db.Set<ContentWorkflowSessionData>()
            .AsNoTracking()
            .Include(x => x.Candidates)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);

        return data is null ? null : ToDomain(data);
    }

    public async Task<WorkflowSession?> GetByIdInternalAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var data = await _db.Set<ContentWorkflowSessionData>()
            .AsNoTracking()
            .Include(x => x.Candidates)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return data is null ? null : ToDomain(data);
    }

    public async Task ReplaceSelectionAsync(
        Guid sessionId, IReadOnlyList<Guid> candidateIds, CancellationToken cancellationToken = default)
    {
        var candidates = await _db.Set<ContentWorkflowCandidateData>()
            .Where(x => x.SessionId == sessionId)
            .ToListAsync(cancellationToken);

        var selected = candidateIds.ToHashSet();
        foreach (var candidate in candidates)
        {
            candidate.IsSelected = selected.Contains(candidate.Id);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetCandidateCompositionAsync(
        Guid candidateId, string compositionJson, string enqueueKey, CancellationToken cancellationToken = default)
    {
        var candidate = await _db.Set<ContentWorkflowCandidateData>()
            .FirstOrDefaultAsync(x => x.Id == candidateId, cancellationToken);

        if (candidate is null)
        {
            return;
        }

        candidate.CompositionJson = compositionJson;
        candidate.EnqueueKey = enqueueKey;
        candidate.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPgUniqueViolation(ex))
        {
            // Defense-in-depth (design decision #6): the Application handler already pre-checks
            // idempotent re-enqueue before calling here (see EnqueueContentWorkflowCandidatesCommandHandler);
            // a race between two concurrent requests for the SAME composition can still reach the DB's
            // partial unique index. Treated as the same idempotent no-op the handler already returns for
            // a sequential duplicate — not a failure the operator needs to see.
            _db.Entry(candidate).State = EntityState.Detached;
        }
    }

    public async Task UpdateCandidateStatusAsync(
        Guid candidateId,
        WorkflowCandidateStatus status,
        Guid? analysisId,
        Guid? batchId,
        string? failureCode,
        CancellationToken cancellationToken = default)
    {
        var candidate = await _db.Set<ContentWorkflowCandidateData>()
            .FirstOrDefaultAsync(x => x.Id == candidateId, cancellationToken);

        if (candidate is null)
        {
            return;
        }

        candidate.Status = status.ToString();
        candidate.FailureCode = failureCode;
        candidate.UpdatedAt = DateTime.UtcNow;

        if (analysisId is not null)
        {
            candidate.AnalysisId = analysisId;
        }

        if (batchId is not null)
        {
            candidate.BatchId = batchId;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task AppendEventAsync(
        WorkflowSessionEvent workflowEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflowEvent);

        _db.Set<ContentWorkflowEventData>().Add(new ContentWorkflowEventData
        {
            Id = workflowEvent.Id,
            SessionId = workflowEvent.SessionId,
            CandidateId = workflowEvent.CandidateId,
            Kind = workflowEvent.Kind,
            MessageKey = workflowEvent.MessageKey,
            DataJson = workflowEvent.DataJson,
            CreatedAt = workflowEvent.CreatedAtUtc,
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowSessionSummary>> ListAsync(
        Guid organizationId, Guid? clientId, int limit, CancellationToken cancellationToken = default)
    {
        var rows = await _db.Set<ContentWorkflowSessionData>()
            .AsNoTracking()
            .Include(x => x.Candidates)
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => clientId == null || x.ClientId == clientId)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new WorkflowSessionSummary(
                x.Id,
                x.ClientId,
                x.ClientName,
                x.Status,
                x.ConfigJson,
                x.Candidates.Count,
                x.Candidates.Count(c => c.IsSelected),
                x.CreatedAt,
                x.UpdatedAt))
            .ToList();
    }

    public async Task AddCandidatesAsync(
        Guid sessionId,
        IReadOnlyList<WorkflowDiscoveredCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var rows = candidates.Select(candidate => new ContentWorkflowCandidateData
        {
            Id = candidate.Id,
            SessionId = sessionId,
            ExternalResultId = candidate.ExternalResultId,
            PayloadJson = candidate.PayloadJson,
            IsSelected = false,
            CompositionJson = "[]",
            EnqueueKey = null,
            // Discovery only persists the row so the operator can select/compose it — it has not been
            // submitted to the real analysis-then-batch pipeline yet. `Queued` is reserved for that
            // genuine submission (see EnqueueContentWorkflowCandidatesCommandHandler), so the workflow
            // history read surface can distinguish "discovered, untouched" from "actually in-flight".
            Status = WorkflowCandidateStatus.Discovered.ToString(),
            QueuedAt = now,
            UpdatedAt = now,
        });

        _db.Set<ContentWorkflowCandidateData>().AddRange(rows);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateSessionStateAsync(
        Guid sessionId, string status, string? configJson, CancellationToken cancellationToken = default)
    {
        var data = await _db.Set<ContentWorkflowSessionData>()
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);

        if (data is null)
        {
            return;
        }

        data.Status = status;
        data.ConfigJson = configJson;
        data.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkflowSessionDetail?> GetDetailAsync(
        Guid id, Guid organizationId, CancellationToken cancellationToken = default)
    {
        var data = await _db.Set<ContentWorkflowSessionData>()
            .AsNoTracking()
            .Include(x => x.Candidates)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);

        return data is null ? null : ToDetail(data);
    }

    public async Task<WorkflowSessionDetail?> GetDetailInternalAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var data = await _db.Set<ContentWorkflowSessionData>()
            .AsNoTracking()
            .Include(x => x.Candidates)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return data is null ? null : ToDetail(data);
    }

    private static WorkflowSessionDetail ToDetail(ContentWorkflowSessionData data) =>
        new(
            data.Id,
            data.OrganizationId,
            data.ClientId,
            data.ClientName,
            data.Status,
            data.ConfigJson,
            data.CreatedAt,
            data.UpdatedAt,
            data.Candidates
                .Select(c => new WorkflowCandidateDetail(c.Id, c.ExternalResultId, c.PayloadJson, c.IsSelected, c.Status, c.FailureCode))
                .ToList());

    private static ContentWorkflowCandidateData ToCandidateData(
        Guid sessionId, WorkflowCandidate candidate, DateTime now) =>
        new()
        {
            Id = candidate.Id,
            SessionId = sessionId,
            ExternalResultId = candidate.ExternalResultId,
            IsSelected = false,
            CompositionJson = "[]",
            EnqueueKey = null,
            Status = candidate.Status.ToString(),
            QueuedAt = now,
            UpdatedAt = now,
        };

    private static WorkflowSession ToDomain(ContentWorkflowSessionData data)
    {
        var session = WorkflowSession.Create(data.Id);

        foreach (var candidateData in data.Candidates)
        {
            var payload = ContentWorkflowCandidatePayload.Parse(candidateData.PayloadJson);
            var sourceUrl = string.IsNullOrWhiteSpace(payload.VideoUrl) ? null : payload.VideoUrl;

            var candidate = WorkflowCandidate.Hydrate(
                candidateData.Id,
                candidateData.ExternalResultId,
                sourceUrl,
                Enum.Parse<WorkflowCandidateStatus>(candidateData.Status),
                DeserializeComposition(candidateData.CompositionJson));

            session.AddCandidate(candidate);
        }

        return session;
    }

    private static IReadOnlyList<(string FormatKey, int Count)> DeserializeComposition(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return [];
        }

        try
        {
            var entries = System.Text.Json.JsonSerializer.Deserialize<List<CompositionEntry>>(json);
            return entries?.Select(e => (e.FormatKey, e.Count)).ToList() ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static bool IsPgUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == "23505";

    private sealed record CompositionEntry(string FormatKey, int Count);
}
