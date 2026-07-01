using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Phase 2 (CAG2-PA-04): the reader surfaces the enriched aesthetic payload when present.
/// WU-2 (CACD-PA-05): the reader threads ConceptualMethod and ImageTypeRatio from the stored
/// blob into the returned <see cref="ClientAestheticProfile"/> (task 2.3 RED / 2.4 GREEN).
/// </summary>
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

    // ── WU-2 (CACD-PA-05): ConceptualMethod + ImageTypeRatio threading ─────

    /// <summary>
    /// CACD-PA-05-a — When the stored blob carries conceptualMethod + imageTypeRatio,
    /// GetProfileAsync returns a ClientAestheticProfile with non-null values for both fields
    /// and no EF/Infrastructure types in the returned profile (task 2.3 RED / 2.4 GREEN).
    /// </summary>
    [Fact]
    public async Task SurfacesConceptualMethodAndImageTypeRatio_WhenPresentInBlob()
    {
        var org = Guid.NewGuid();
        var client = Guid.NewGuid();

        var conceptualMethod = new ConceptualMethod(
            Method: ArtDirectionMethod.MetaphoricalEditorial,
            Rationale: "message becomes visual metaphor",
            Cues: ["mono-color backdrop", "object-as-metaphor"]);

        var imageTypeRatio = new ImageTypeRatio(
            Photo: 0.5,
            FlatColor: 0.4,
            Decorator: 0.1,
            SampleSize: 10);

        var json = AestheticProfilePersistence.Serialize(
            new AestheticExtraction(
                Palette: [],
                StyleDescriptors: ["editorial"],
                Motifs: [],
                CaptionVoice: "playful",
                ConceptualMethod: conceptualMethod,
                ImageTypeRatio: imageTypeRatio),
            referenceImages: []);

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

        // ConceptualMethod threaded through
        Assert.NotNull(profile!.ConceptualMethod);
        Assert.Equal(ArtDirectionMethod.MetaphoricalEditorial, profile.ConceptualMethod!.Method);
        Assert.Equal("message becomes visual metaphor", profile.ConceptualMethod.Rationale);
        Assert.Equal(2, profile.ConceptualMethod.Cues.Count);
        Assert.Contains("mono-color backdrop", profile.ConceptualMethod.Cues);

        // ImageTypeRatio threaded through
        Assert.NotNull(profile.ImageTypeRatio);
        Assert.Equal(0.5, profile.ImageTypeRatio!.Photo);
        Assert.Equal(0.4, profile.ImageTypeRatio.FlatColor);
        Assert.Equal(0.1, profile.ImageTypeRatio.Decorator);
        Assert.Equal(10, profile.ImageTypeRatio.SampleSize);

        // Architecture gate: returned type has no EF/Supabase type in its signature
        Assert.IsType<ClientAestheticProfile>(profile);
        var profileType = profile.GetType();
        Assert.DoesNotContain("EntityFramework", profileType.FullName ?? string.Empty);
        Assert.DoesNotContain("Npgsql", profileType.FullName ?? string.Empty);
    }

    /// <summary>
    /// CACD-PA-05-b — When the stored blob is a pre-v2 blob (no conceptualMethod / imageTypeRatio
    /// keys), GetProfileAsync returns a profile with both fields null and no exception is thrown
    /// (cold-start parity; task 2.3 RED / 2.4 GREEN).
    /// </summary>
    [Fact]
    public async Task ConceptualMethodAndImageTypeRatioNull_WhenPreV2Blob_NoException()
    {
        var org = Guid.NewGuid();
        var client = Guid.NewGuid();

        // Pre-v2 blob: no conceptualMethod or imageTypeRatio keys
        const string preV2Json =
            """{"palette":[],"styleDescriptors":["editorial"],"motifs":[],"captionVoice":"playful","referenceImages":[]}""";

        await using var db = CreateDb();
        db.ProfileAnalyses.Add(new ProfileAnalysisData
        {
            Id = Guid.NewGuid(),
            OrganizationId = org,
            ClientId = client,
            ProfileUrlOrHandle = "@client",
            Status = "Analyzed",
            AestheticProfileJson = preV2Json,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var profile = await new EfClientAestheticProfileReader(db).GetProfileAsync(org, client, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Null(profile!.ConceptualMethod);
        Assert.Null(profile.ImageTypeRatio);
        // Existing fields still surface correctly from the pre-v2 blob
        Assert.NotNull(profile.StyleDescriptors);
        Assert.Contains("editorial", profile.StyleDescriptors!);
    }
}
