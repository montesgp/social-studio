using SocialStudio.Domain.Social.ContentStudio;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using SocialStudio.Infrastructure.Persistence.Postgres.Maintenance;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence;

/// <summary>
/// TDD tests for <see cref="ContentStudioCaptionBackfill"/> — task 3.6 (Content Studio editing
/// completeness, Decision E4). Idempotent: running twice produces an identical end state.
/// </summary>
public sealed class ContentStudioCaptionBackfillTests
{
    [Fact]
    public async Task RunAsync_BackfillsDraftCaptionFromDraftText_ForVisualItemsOnly()
    {
        await using var db = CreateDbContext();

        var visualItem = NewRow(ContentMedium.Visual, draftText: "Grow your brand #growth", draftCaption: null);
        var reelItem = NewRow(ContentMedium.Text, draftText: "Reel script line one", draftCaption: null);
        var alreadyCaptioned = NewRow(ContentMedium.Visual, draftText: "Old text", draftCaption: "Existing caption");

        db.ContentStudioItems.AddRange(visualItem, reelItem, alreadyCaptioned);
        await db.SaveChangesAsync(CancellationToken.None);

        var backfill = new ContentStudioCaptionBackfill(db);
        var updated = await backfill.RunAsync(CancellationToken.None);

        Assert.Equal(1, updated);

        var reloadedVisual = await db.ContentStudioItems.AsNoTracking().SingleAsync(x => x.Id == visualItem.Id);
        Assert.Equal("Grow your brand #growth", reloadedVisual.DraftCaption);

        var reloadedReel = await db.ContentStudioItems.AsNoTracking().SingleAsync(x => x.Id == reelItem.Id);
        Assert.Null(reloadedReel.DraftCaption); // reel scripts are never backfilled

        var reloadedAlready = await db.ContentStudioItems.AsNoTracking().SingleAsync(x => x.Id == alreadyCaptioned.Id);
        Assert.Equal("Existing caption", reloadedAlready.DraftCaption); // untouched, already had a caption
    }

    [Fact]
    public async Task RunAsync_IsIdempotent_SecondRunUpdatesNothing_SameEndState()
    {
        await using var db = CreateDbContext();
        var visualItem = NewRow(ContentMedium.Visual, draftText: "Grow your brand #growth", draftCaption: null);
        db.ContentStudioItems.Add(visualItem);
        await db.SaveChangesAsync(CancellationToken.None);

        var backfill = new ContentStudioCaptionBackfill(db);
        var firstRun = await backfill.RunAsync(CancellationToken.None);
        var secondRun = await backfill.RunAsync(CancellationToken.None);

        Assert.Equal(1, firstRun);
        Assert.Equal(0, secondRun);

        var reloaded = await db.ContentStudioItems.AsNoTracking().SingleAsync(x => x.Id == visualItem.Id);
        Assert.Equal("Grow your brand #growth", reloaded.DraftCaption);
    }

    private static ContentStudioItemData NewRow(ContentMedium medium, string? draftText, string? draftCaption) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            GeneratorKind = 0,
            SourceRunId = Guid.NewGuid(),
            FormatResultId = Guid.NewGuid(),
            FormatKey = "carousel",
            ContentMedium = (short)medium,
            DraftText = draftText,
            DraftCaption = draftCaption,
            TargetNetwork = "instagram",
            TargetPlacement = "feed",
            ReviewState = 0,
            VariantGroupId = Guid.NewGuid(),
            IsPrimaryVariant = true,
            EditLastSource = 0,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"caption-backfill-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
