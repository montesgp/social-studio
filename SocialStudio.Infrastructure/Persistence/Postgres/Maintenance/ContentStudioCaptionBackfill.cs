using SocialStudio.Domain.Social.ContentStudio;
using SocialStudio.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres.Maintenance;

/// <summary>
/// Backfills <c>draft_caption</c> from <c>draft_text</c> for pre-existing Content Studio items
/// (Decision E4, task 3.6 — Content Studio editing completeness). Deliberately kept OUTSIDE the
/// schema migration (<c>Add_ContentStudioDraftCaption</c>): a data-shape heuristic like this must
/// stay re-runnable and reversible without a schema rollback.
/// <para>
/// "Caption-shaped" = <see cref="ContentMedium.Visual"/> (carousel/story/feed-post). Reel-script
/// items resolve to <see cref="ContentMedium.Text"/> per <c>ContentFormatCatalog.MediumFor</c> and
/// are never touched — <c>DraftText</c> keeps carrying their script, <c>DraftCaption</c> stays null
/// until <c>MaterializeRunCommandHandler</c>'s Decision E3 fallback fills it on next materialization.
/// </para>
/// <para>
/// Idempotent by construction: the WHERE clause only matches rows with no caption yet, so a second
/// run finds nothing left to update and returns 0 — the same observable end state as the first run.
/// </para>
/// </summary>
public sealed class ContentStudioCaptionBackfill
{
    private readonly AppIdentityDbContext _db;

    public ContentStudioCaptionBackfill(AppIdentityDbContext db)
    {
        _db = db;
    }

    /// <summary>Runs the backfill. Returns the number of rows updated (0 on a repeat run).</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var candidates = await _db.ContentStudioItems
            .Where(x => x.ContentMedium == (short)ContentMedium.Visual
                        && x.DraftCaption == null
                        && x.DraftText != null)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return 0;
        }

        foreach (var row in candidates)
        {
            row.DraftCaption = row.DraftText;
        }

        await _db.SaveChangesAsync(ct);
        return candidates.Count;
    }
}
