using Incoders.Hive.Domain.Social.ContentStudio;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

/// <summary>
/// EF mapping + repository round-trip tests for the Content Studio tables.
/// Exercises the domain↔data mapping (enum→smallint, jsonb assets, owned edit-trace/plan-context).
/// </summary>
public sealed class ContentStudioEntityMappingTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ContentItem_RoundTrips_AllFields_IncludingJsonbAssets()
    {
        await using var db = CreateDbContext();
        var repo = new EfContentItemRepository(db);

        var id = ContentItemId.New();
        var origin = new ContentOrigin(GeneratorKind.SingleAnalyzer, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "carousel");
        var kind = new ContentKind("carousel", ContentMedium.Visual);
        var assets = new List<ContentAssetRef>
        {
            new("rendered", "storage://org/job/carousel/slide_1.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
        };
        var draft = new ContentDraft("caption text", "<spec/>", assets, new TargetChannel("instagram", "carousel"));
        var item = ContentItem.Restore(
            id, Org, Client, origin, kind, draft,
            ReviewState.Edited, EditTrace.ManualEdit(Now), id.Value, true,
            null, null, null, Now, Now);

        await repo.AddRangeAsync(new[] { item }, CancellationToken.None);

        var read = await repo.GetByIdAsync(Org, id, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(GeneratorKind.SingleAnalyzer, read!.Origin.GeneratorKind);
        Assert.Equal(origin.SourcePackageId, read.Origin.SourcePackageId);
        Assert.Equal("carousel", read.Origin.FormatKey);
        Assert.Equal(ContentMedium.Visual, read.Kind.Medium);
        Assert.Equal("caption text", read.Draft.Text);
        Assert.Equal("<spec/>", read.Draft.VisualSpecMarkup);
        Assert.Single(read.Draft.Assets);
        Assert.Equal("storage://org/job/carousel/slide_1.pptx", read.Draft.Assets[0].StorageUri);
        Assert.Equal(new TargetChannel("instagram", "carousel"), read.Draft.TargetChannel);
        Assert.Equal(ReviewState.Edited, read.State);
        Assert.Equal(EditSource.Manual, read.EditTrace.LastEditedSource);
        Assert.Equal(Now, read.EditTrace.LastEditedAtUtc);
    }

    [Fact]
    public async Task ContentItem_PlanContext_RoundTrips()
    {
        await using var db = CreateDbContext();
        var repo = new EfContentItemRepository(db);

        var id = ContentItemId.New();
        var plan = new PlanContext(Guid.NewGuid(), new PeriodRef(0, 2026, 6), Guid.NewGuid());
        var reviewer = Guid.NewGuid();
        var item = ContentItem.Restore(
            id, Org, Client, NewOrigin(), new ContentKind("caption", ContentMedium.Text),
            ContentDraft.ForText("x", new TargetChannel("instagram", "feed")),
            ReviewState.Approved, EditTrace.Initial(), id.Value, true,
            plan, null, reviewer, Now, Now);

        await repo.AddRangeAsync(new[] { item }, CancellationToken.None);

        var read = await repo.GetByIdAsync(Org, id, CancellationToken.None);

        Assert.NotNull(read!.PlanContext);
        Assert.Equal(plan.ContentPlanId, read.PlanContext!.ContentPlanId);
        Assert.Equal(plan.Period, read.PlanContext.Period);
        Assert.Equal(plan.CalendarSlotId, read.PlanContext.CalendarSlotId);
        Assert.Equal(reviewer, read.ReviewerUserId);
    }

    [Fact]
    public async Task ContentItem_WithoutPlanContext_RoundTripsAsNull()
    {
        await using var db = CreateDbContext();
        var repo = new EfContentItemRepository(db);
        var id = ContentItemId.New();
        var item = ContentItem.Materialize(
            id, Org, Client, NewOrigin(), new ContentKind("caption", ContentMedium.Text),
            ContentDraft.ForText("x", new TargetChannel("instagram", "feed")), Now).Value;

        await repo.AddRangeAsync(new[] { item }, CancellationToken.None);
        var read = await repo.GetByIdAsync(Org, id, CancellationToken.None);

        Assert.Null(read!.PlanContext);
        Assert.Equal(ReviewState.Draft, read.State);
    }

    [Fact]
    public async Task ContentSet_RoundTrips_WithMembers_Summary_AndCrossOrgIsolation()
    {
        await using var db = CreateDbContext();
        var setRepo = new EfContentSetRepository(db);
        var itemRepo = new EfContentItemRepository(db);

        var runId = Guid.NewGuid();
        var setId = ContentSetId.New();
        var set = ContentSet.CreateForRun(setId, Org, Client, SetOrigin.ForRun(GeneratorKind.SingleAnalyzer, runId), Now).Value;

        var items = new List<ContentItem>();
        for (var i = 0; i < 3; i++)
        {
            var itemId = ContentItemId.New();
            var state = i == 2 ? ReviewState.Approved : ReviewState.Draft;
            items.Add(ContentItem.Restore(
                itemId, Org, Client, NewOrigin(runId), new ContentKind("caption", ContentMedium.Text),
                ContentDraft.ForText("t", new TargetChannel("instagram", "feed")),
                state, EditTrace.Initial(), itemId.Value, true, null, null, null, Now, Now));
            set.AddMember(itemId, i);
        }

        await itemRepo.AddRangeAsync(items, CancellationToken.None);
        await setRepo.AddAsync(set, CancellationToken.None);

        var readSet = await setRepo.GetByRunAsync(Org, runId, CancellationToken.None);
        Assert.NotNull(readSet);
        Assert.Equal(SetKind.GenerationRun, readSet!.Kind);
        Assert.Equal(GeneratorKind.SingleAnalyzer, readSet.Origin.GeneratorKind);
        Assert.Equal(runId, readSet.Origin.SourceRunId);
        Assert.Equal(3, readSet.Members.Count);

        var summary = await setRepo.GetReviewSummaryAsync(Org, setId, CancellationToken.None);
        Assert.Equal(3, summary.Total);
        Assert.Equal(2, summary.Draft);
        Assert.Equal(1, summary.Approved);

        var bySet = await itemRepo.ListBySetAsync(Org, setId, CancellationToken.None);
        Assert.Equal(3, bySet.Count);

        // Cross-organization isolation: another org sees nothing
        var otherOrg = await setRepo.GetByRunAsync(Guid.NewGuid(), runId, CancellationToken.None);
        Assert.Null(otherOrg);
    }

    [Fact]
    public async Task ContentSet_UpdateAsync_RetriesOnceOnConcurrencyAndPersistsNewMember()
    {
        var interceptor = new ThrowOnceContentSetConcurrencyInterceptor();
        await using var db = CreateDbContext(interceptor);
        var repo = new EfContentSetRepository(db);

        var runId = Guid.NewGuid();
        var setId = ContentSetId.New();
        var firstItemId = ContentItemId.New();
        var secondItemId = ContentItemId.New();
        var set = ContentSet.CreateForRun(setId, Org, Client, SetOrigin.ForRun(GeneratorKind.SingleAnalyzer, runId), Now).Value;
        set.AddMember(firstItemId, 0);
        await repo.AddAsync(set, CancellationToken.None);
        db.ChangeTracker.Clear();

        set.AddMember(secondItemId, 1);
        interceptor.Arm();

        await repo.UpdateAsync(set, CancellationToken.None);

        var read = await repo.GetByIdAsync(Org, setId, CancellationToken.None);
        Assert.NotNull(read);
        Assert.Equal(1, interceptor.ThrowCount);
        Assert.Equal(2, read!.Members.Count);
        Assert.Contains(read.Members, member => member.ItemId == secondItemId && member.SortOrder == 1);
    }

    [Fact]
    public async Task UpdateAsync_PersistsLifecycleAndDraftChanges()
    {
        await using var db = CreateDbContext();
        var repo = new EfContentItemRepository(db);
        var id = ContentItemId.New();
        var item = ContentItem.Materialize(
            id, Org, Client, NewOrigin(), new ContentKind("caption", ContentMedium.Text),
            ContentDraft.ForText("before", new TargetChannel("instagram", "feed")), Now).Value;
        await repo.AddRangeAsync(new[] { item }, CancellationToken.None);

        item.EditText("after", maxLength: 100, Now);
        await repo.UpdateAsync(item, CancellationToken.None);

        var read = await repo.GetByIdAsync(Org, id, CancellationToken.None);
        Assert.Equal("after", read!.Draft.Text);
        Assert.Equal(ReviewState.Edited, read.State);
    }

    // -----------------------------------------------------------------------
    // WU-6 §6.7 — SocialClientData.background_sourcing_policy nullable column round-trip
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SocialClient_BackgroundSourcingPolicy_RoundTrips_NullAndValue()
    {
        // Arrange: client with an explicit policy and one without (NULL == legacy)
        await using var db = CreateDbContext();

        var clientWithPolicy = new Incoders.Hive.Infrastructure.Persistence.Postgres.Entities.SocialClientData
        {
            Id = Guid.NewGuid(),
            OrganizationId = Org,
            Name = "Policy Client",
            IsActive = true,
            BackgroundSourcingPolicy = 1, // ClientDefinedSet
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        };
        var clientWithoutPolicy = new Incoders.Hive.Infrastructure.Persistence.Postgres.Entities.SocialClientData
        {
            Id = Guid.NewGuid(),
            OrganizationId = Org,
            Name = "Legacy Client",
            IsActive = true,
            BackgroundSourcingPolicy = null,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        };

        db.SocialClients.AddRange(clientWithPolicy, clientWithoutPolicy);
        await db.SaveChangesAsync();

        // Act
        var readWithPolicy = await db.SocialClients.AsNoTracking()
            .FirstAsync(x => x.Id == clientWithPolicy.Id);
        var readWithoutPolicy = await db.SocialClients.AsNoTracking()
            .FirstAsync(x => x.Id == clientWithoutPolicy.Id);

        // Assert
        Assert.Equal((short)1, readWithPolicy.BackgroundSourcingPolicy);
        Assert.Null(readWithoutPolicy.BackgroundSourcingPolicy);
    }

    // -----------------------------------------------------------------------
    // CAG-CS-01 — SocialClientData.aesthetic_refresh_threshold_days nullable column round-trip
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SocialClient_AestheticRefreshThresholdDays_RoundTrips_NullAndValue()
    {
        await using var db = CreateDbContext();

        var clientWithThreshold = new Incoders.Hive.Infrastructure.Persistence.Postgres.Entities.SocialClientData
        {
            Id = Guid.NewGuid(),
            OrganizationId = Org,
            Name = "Threshold Client",
            IsActive = true,
            AestheticRefreshThresholdDays = 14,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        };
        var clientWithoutThreshold = new Incoders.Hive.Infrastructure.Persistence.Postgres.Entities.SocialClientData
        {
            Id = Guid.NewGuid(),
            OrganizationId = Org,
            Name = "Default Threshold Client",
            IsActive = true,
            AestheticRefreshThresholdDays = null,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        };

        db.SocialClients.AddRange(clientWithThreshold, clientWithoutThreshold);
        await db.SaveChangesAsync();

        var readWith = await db.SocialClients.AsNoTracking()
            .FirstAsync(x => x.Id == clientWithThreshold.Id);
        var readWithout = await db.SocialClients.AsNoTracking()
            .FirstAsync(x => x.Id == clientWithoutThreshold.Id);

        Assert.Equal((short)14, readWith.AestheticRefreshThresholdDays);
        Assert.Null(readWithout.AestheticRefreshThresholdDays);
    }

    private static ContentOrigin NewOrigin(Guid? runId = null) =>
        new(GeneratorKind.SingleAnalyzer, runId ?? Guid.NewGuid(), null, Guid.NewGuid(), "caption");

    private static AppIdentityDbContext CreateDbContext(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"content-studio-{Guid.NewGuid():N}");

        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        var options = builder.Options;
        return new AppIdentityDbContext(options);
    }

    private sealed class ThrowOnceContentSetConcurrencyInterceptor : SaveChangesInterceptor
    {
        private bool _armed;

        public int ThrowCount { get; private set; }

        public void Arm()
        {
            _armed = true;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_armed && eventData.Context is not null && HasContentSetChanges(eventData.Context))
            {
                _armed = false;
                ThrowCount++;
                throw new DbUpdateConcurrencyException("Injected content set concurrency conflict.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private static bool HasContentSetChanges(DbContext context) =>
            context.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && entry.Entity is ContentStudioSetData or ContentStudioSetItemData);
    }
}
