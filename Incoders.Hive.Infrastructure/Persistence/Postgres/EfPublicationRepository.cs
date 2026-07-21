using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Domain.Social.Publications;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfPublicationRepository : IPublicationRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfPublicationRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Publication?> GetByIdAsync(Guid organizationId, PublicationId id, CancellationToken ct)
    {
        var row = await _dbContext.Publications
            .Include(x => x.Attempts)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value && x.OrganizationId == organizationId, ct);

        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<Publication>> ListByContentItemsAsync(
        Guid organizationId, IReadOnlyList<Guid> contentItemIds, CancellationToken ct)
    {
        if (contentItemIds.Count == 0)
        {
            return [];
        }

        var ids = contentItemIds.ToHashSet();
        var rows = await _dbContext.Publications
            .Include(x => x.Attempts)
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.ContentItemId))
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Publication>> ListByClientAsync(
        Guid organizationId, Guid clientId, CancellationToken ct)
    {
        var rows = await _dbContext.Publications
            .Include(x => x.Attempts)
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ClientId == clientId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task AddAsync(Publication publication, CancellationToken ct)
    {
        _dbContext.Publications.Add(ToData(publication));
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Publication publication, CancellationToken ct)
    {
        var existing = await _dbContext.Publications
            .Include(x => x.Attempts)
            .FirstOrDefaultAsync(x => x.Id == publication.Id.Value && x.OrganizationId == publication.OrganizationId, ct);

        if (existing is null)
        {
            return;
        }

        MapToExisting(publication, existing);
        await _dbContext.SaveChangesAsync(ct);
    }

    // ─── Mapping helpers ──────────────────────────────────────────────────────

    private static Publication ToDomain(PublicationData d)
    {
        var destination = new PublicationDestination(
            d.DestinationClientAccountConnectionId, d.DestinationSocialNetworkId, d.DestinationNetworkKey);

        var attempts = d.Attempts
            .OrderBy(a => a.AttemptNumber)
            .Select(a => new PublicationAttempt(
                a.AttemptNumber, a.StartedAtUtc, a.FinishedAtUtc,
                (PublicationAttemptOutcome)a.Outcome, a.ErrorMessage, a.ExternalReference))
            .ToList();

        return Publication.Restore(
            new PublicationId(d.Id),
            d.OrganizationId,
            d.ClientId,
            d.ContentItemId,
            destination,
            (PublicationStatus)d.Status,
            d.ScheduledForUtc,
            attempts,
            d.ExternalReference,
            d.CreatedAtUtc,
            d.UpdatedAtUtc);
    }

    private static PublicationData ToData(Publication publication)
    {
        var d = new PublicationData { Id = publication.Id.Value };
        MapToExisting(publication, d);
        d.CreatedAtUtc = publication.CreatedAtUtc;
        return d;
    }

    private static void MapToExisting(Publication publication, PublicationData d)
    {
        d.OrganizationId = publication.OrganizationId;
        d.ClientId = publication.ClientId;
        d.ContentItemId = publication.ContentItemId;
        d.DestinationClientAccountConnectionId = publication.Destination.ClientAccountConnectionId;
        d.DestinationSocialNetworkId = publication.Destination.SocialNetworkId;
        d.DestinationNetworkKey = publication.Destination.NetworkKey;
        d.Status = (short)publication.Status;
        d.ScheduledForUtc = publication.ScheduledForUtc;
        d.ExternalReference = publication.ExternalReference;
        d.UpdatedAtUtc = publication.UpdatedAtUtc;

        SyncAttempts(publication, d);
    }

    private static void SyncAttempts(Publication publication, PublicationData d)
    {
        var existingByNumber = d.Attempts.ToDictionary(a => a.AttemptNumber);

        foreach (var attempt in publication.Attempts)
        {
            if (existingByNumber.TryGetValue(attempt.AttemptNumber, out var row))
            {
                row.StartedAtUtc = attempt.StartedAtUtc;
                row.FinishedAtUtc = attempt.FinishedAtUtc;
                row.Outcome = (short)attempt.Outcome;
                row.ErrorMessage = attempt.ErrorMessage;
                row.ExternalReference = attempt.ExternalReference;
            }
            else
            {
                d.Attempts.Add(new PublicationAttemptData
                {
                    Id = Guid.NewGuid(),
                    PublicationId = d.Id,
                    AttemptNumber = attempt.AttemptNumber,
                    StartedAtUtc = attempt.StartedAtUtc,
                    FinishedAtUtc = attempt.FinishedAtUtc,
                    Outcome = (short)attempt.Outcome,
                    ErrorMessage = attempt.ErrorMessage,
                    ExternalReference = attempt.ExternalReference,
                });
            }
        }
    }
}
