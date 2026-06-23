using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>Phase 2 (CAG2-PA-04): the reader surfaces the enriched aesthetic payload when present.</summary>
public sealed class EfClientAestheticProfileReaderEnrichedTests
{
    private static AppIdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"aesthetic-reader-{Guid.NewGuid():N}")
            .Options);

    [Fact]
    public async Task SurfacesEnrichedPayload_WhenPresent()
    {
        var org = Guid.NewGuid();
        var client = Guid.NewGuid();
        var json = AestheticProfilePersistence.Serialize(
            new AestheticExtraction(
                Palette: [new AestheticPaletteEntry("#C0FFEE", "Primary", 1)],
                StyleDescriptors: ["editorial, airy"],
                Motifs: ["film grain"],
                CaptionVoice: "lowercase, playful"),
            referenceImages: [new ProfilePostImageRef("p1", 0, "org/c/a/aesthetic/p1-0.jpg", "h1")]);

        await using var db = CreateDb();
        db.ProfileAnalyses.Add(new ProfileAnalysisData
        {
            Id = Guid.NewGuid(),
            OrganizationId = org,
            ClientId = client,
            ProfileUrlOrHandle = "@client",
            Status = "Analyzed",
            AestheticProfileJson = json,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var profile = await new EfClientAestheticProfileReader(db).GetProfileAsync(org, client, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Single(profile!.Palette!);
        Assert.Equal("#C0FFEE", profile.Palette![0].Hex);
        Assert.Contains("editorial, airy", profile.StyleDescriptors!);
        Assert.Contains("film grain", profile.Motifs!);
        Assert.Equal("lowercase, playful", profile.CaptionVoice);
        Assert.Single(profile.ReferenceImages!);
        Assert.Equal("org/c/a/aesthetic/p1-0.jpg", profile.ReferenceImages![0].StoragePath);
    }

    [Fact]
    public async Task EnrichedFieldsNull_WhenNoAestheticJson()
    {
        var org = Guid.NewGuid();
        var client = Guid.NewGuid();

        await using var db = CreateDb();
        db.ProfileAnalyses.Add(new ProfileAnalysisData
        {
            Id = Guid.NewGuid(),
            OrganizationId = org,
            ClientId = client,
            ProfileUrlOrHandle = "@client",
            Status = "Analyzed",
            AestheticProfileJson = null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var profile = await new EfClientAestheticProfileReader(db).GetProfileAsync(org, client, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Null(profile!.Palette);
        Assert.Null(profile.ReferenceImages);
    }
}
