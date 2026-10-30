using SocialStudio.Domain.Social.Publications;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence;

/// <summary>
/// EF mapping + repository round-trip tests for <c>social.publications</c> /
/// <c>social.publication_attempts</c>. Exercises enum→smallint conversions and the owned
/// one-to-many attempts collection (Restore ↔ persist).
/// </summary>
public sealed class PublicationEntityMappingTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Publication_RoundTrips_WithTwoOrMoreAttempts_AndEnumConversions()
    {
        await using var db = CreateDbContext();
        var repo = new EfPublicationRepository(db);

        var destination = new PublicationDestination(Guid.NewGuid(), Guid.NewGuid(), "instagram");
        var publication = Publication.PublishNow(
            PublicationId.New(), Org, Client, Guid.NewGuid(), destination, Now).Value;
        publication.MarkFailed("provider error", Now.AddSeconds(5));
        publication.Retry(Now.AddSeconds(10));

        await repo.AddAsync(publication, CancellationToken.None);

        var read = await repo.GetByIdAsync(Org, publication.Id, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(PublicationStatus.Publishing, read!.Status);
        Assert.Equal(destination, read.Destination);
        Assert.Equal(2, read.AttemptCount);
        Assert.Equal(PublicationAttemptOutcome.Failed, read.Attempts[0].Outcome);
        Assert.Equal("provider error", read.Attempts[0].ErrorMessage);
        Assert.Equal(PublicationAttemptOutcome.Pending, read.Attempts[1].Outcome);
        Assert.Equal(1, read.Attempts[0].AttemptNumber);
        Assert.Equal(2, read.Attempts[1].AttemptNumber);
    }

    [Fact]
    public async Task Publication_ContentItemId_HasNoForeignKeyConstraint_OrphanTolerant()
    {
        await using var db = CreateDbContext();
        var repo = new EfPublicationRepository(db);

        // A ContentItemId that references no existing content_studio_items row must still persist.
        var orphanContentItemId = Guid.NewGuid();
        var destination = new PublicationDestination(Guid.NewGuid(), Guid.NewGuid(), "instagram");
        var publication = Publication.PublishNow(
            PublicationId.New(), Org, Client, orphanContentItemId, destination, Now).Value;

        await repo.AddAsync(publication, CancellationToken.None);
        var read = await repo.GetByIdAsync(Org, publication.Id, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(orphanContentItemId, read!.ContentItemId);

        var entityType = db.Model.FindEntityType(typeof(PublicationData))!;
        Assert.DoesNotContain(
            entityType.GetForeignKeys(),
            fk => fk.Properties.Any(p => p.Name == nameof(PublicationData.ContentItemId)));
    }

    [Fact]
    public async Task Publication_UpdateAsync_PersistsStatusAndExternalReference()
    {
        await using var db = CreateDbContext();
        var repo = new EfPublicationRepository(db);

        var destination = new PublicationDestination(Guid.NewGuid(), Guid.NewGuid(), "instagram");
        var publication = Publication.PublishNow(
            PublicationId.New(), Org, Client, Guid.NewGuid(), destination, Now).Value;
        await repo.AddAsync(publication, CancellationToken.None);

        publication.MarkPublished("media-999", Now.AddSeconds(30));
        await repo.UpdateAsync(publication, CancellationToken.None);

        var read = await repo.GetByIdAsync(Org, publication.Id, CancellationToken.None);
        Assert.Equal(PublicationStatus.Published, read!.Status);
        Assert.Equal("media-999", read.ExternalReference);
        Assert.Equal(PublicationAttemptOutcome.Succeeded, read.Attempts[0].Outcome);
    }

    [Fact]
    public async Task ListByClientAsync_ReturnsAllPublicationsForClient()
    {
        await using var db = CreateDbContext();
        var repo = new EfPublicationRepository(db);
        var destination = new PublicationDestination(Guid.NewGuid(), Guid.NewGuid(), "instagram");

        var first = Publication.PublishNow(PublicationId.New(), Org, Client, Guid.NewGuid(), destination, Now).Value;
        var second = Publication.PublishNow(PublicationId.New(), Org, Client, Guid.NewGuid(), destination, Now).Value;
        await repo.AddAsync(first, CancellationToken.None);
        await repo.AddAsync(second, CancellationToken.None);

        var results = await repo.ListByClientAsync(Org, Client, CancellationToken.None);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task ListByContentItemsAsync_ReturnsPublicationsMatchingContentItemIds()
    {
        await using var db = CreateDbContext();
        var repo = new EfPublicationRepository(db);
        var destination = new PublicationDestination(Guid.NewGuid(), Guid.NewGuid(), "instagram");

        var targetItemId = Guid.NewGuid();
        var target = Publication.PublishNow(PublicationId.New(), Org, Client, targetItemId, destination, Now).Value;
        var other = Publication.PublishNow(PublicationId.New(), Org, Client, Guid.NewGuid(), destination, Now).Value;
        await repo.AddAsync(target, CancellationToken.None);
        await repo.AddAsync(other, CancellationToken.None);

        var results = await repo.ListByContentItemsAsync(Org, new[] { targetItemId }, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(targetItemId, results[0].ContentItemId);
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"publications-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
