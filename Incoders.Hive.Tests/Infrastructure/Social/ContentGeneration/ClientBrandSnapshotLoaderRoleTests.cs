using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Tests that EfClientBrandSnapshotLoader correctly loads BackgroundSourcingPolicy from the
/// SocialClientData row, maps ColorEntry→WeightedColor with Role and Rank, pre-sorts by
/// prevalence (Role ordinal, Rank), and propagates Client.BackgroundSourcingPolicy to the
/// ClientProfileSnapshot.
///
/// Refs: design §6 item 7; tasks 6.4; CG-COL-02, BSP-07.
/// </summary>
public sealed class ClientBrandSnapshotLoaderRoleTests : IDisposable
{
    private readonly AppIdentityDbContext _db;
    private readonly EfClientBrandSnapshotLoader _loader;

    public ClientBrandSnapshotLoaderRoleTests()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppIdentityDbContext(options);
        _loader = new EfClientBrandSnapshotLoader(_db);
    }

    public void Dispose() => _db.Dispose();

    // -----------------------------------------------------------------------
    // 6.4a — palette entries are mapped to WeightedColor carrying Role and Rank
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Load_ColorPaletteEntries_AreMapppedToWeightedColorWithRoleAndRank()
    {
        var (orgId, clientId) = SeedClient(BackgroundSourcingPolicy.AiPalette);
        SeedBrandManual(orgId, clientId, new List<ColorEntry>
        {
            new("Primary", "#FF0000", ColorRole.Primary, 0),
            new("Accent", "#0000FF", ColorRole.Accent, 1),
        });
        await _db.SaveChangesAsync();

        var snapshot = await _loader.LoadAsync(orgId, clientId);

        Assert.NotNull(snapshot.Brand);
        Assert.Equal(2, snapshot.Brand!.ColorPalette.Count);

        // Pre-sorted: Primary (ordinal 0) before Accent (ordinal 2)
        var first = snapshot.Brand.ColorPalette[0];
        Assert.Equal("#FF0000", first.Hex);
        Assert.Equal(ColorRole.Primary, first.Role);
        Assert.Equal(0, first.Rank);

        var second = snapshot.Brand.ColorPalette[1];
        Assert.Equal("#0000FF", second.Hex);
        Assert.Equal(ColorRole.Accent, second.Role);
        Assert.Equal(1, second.Rank);
    }

    // -----------------------------------------------------------------------
    // 6.4b — palette is pre-sorted by (Role ordinal, Rank) — CG-COL-02
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Load_ColorPalette_IsPreSortedByRoleOrdinalThenRank()
    {
        var (orgId, clientId) = SeedClient(null);
        SeedBrandManual(orgId, clientId, new List<ColorEntry>
        {
            // Deliberately stored out-of-order to verify sorting
            new("Neutral-1", "#CCCCCC", ColorRole.Neutral, 0),
            new("Accent-2", "#FF8800", ColorRole.Accent, 1),
            new("Primary-1", "#FF0000", ColorRole.Primary, 0),
            new("Accent-1", "#FF6600", ColorRole.Accent, 0),
            new("Secondary-1", "#00FF00", ColorRole.Secondary, 0),
        });
        await _db.SaveChangesAsync();

        var snapshot = await _loader.LoadAsync(orgId, clientId);

        Assert.NotNull(snapshot.Brand);
        var palette = snapshot.Brand!.ColorPalette;
        Assert.Equal(5, palette.Count);

        // Expected order: Primary(0,0), Secondary(1,0), Accent(2,0), Accent(2,1), Neutral(3,0)
        Assert.Equal(ColorRole.Primary, palette[0].Role);
        Assert.Equal(ColorRole.Secondary, palette[1].Role);
        Assert.Equal(ColorRole.Accent, palette[2].Role);
        Assert.Equal(0, palette[2].Rank);
        Assert.Equal(ColorRole.Accent, palette[3].Role);
        Assert.Equal(1, palette[3].Rank);
        Assert.Equal(ColorRole.Neutral, palette[4].Role);
    }

    // -----------------------------------------------------------------------
    // 6.4c — Client.BackgroundSourcingPolicy is loaded into ClientProfileSnapshot
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Load_WithPolicySet_ClientProfileSnapshotCarriesPolicy()
    {
        var (orgId, clientId) = SeedClient(BackgroundSourcingPolicy.ClientDefinedSet);
        await _db.SaveChangesAsync();

        var snapshot = await _loader.LoadAsync(orgId, clientId);

        Assert.NotNull(snapshot.Client);
        Assert.Equal(BackgroundSourcingPolicy.ClientDefinedSet, snapshot.Client!.BackgroundSourcingPolicy);
    }

    // -----------------------------------------------------------------------
    // 6.4d — null policy (UNSET) flows through to ClientProfileSnapshot as null
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Load_WithNullPolicy_ClientProfileSnapshotHasNullPolicy()
    {
        var (orgId, clientId) = SeedClient(null);
        await _db.SaveChangesAsync();

        var snapshot = await _loader.LoadAsync(orgId, clientId);

        Assert.NotNull(snapshot.Client);
        Assert.Null(snapshot.Client!.BackgroundSourcingPolicy);
    }

    // -----------------------------------------------------------------------
    // 6.4e — null Role entries sort after classified roles (legacy compat)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Load_ColorPaletteWithNullRoleEntries_NullRoleSortsLast()
    {
        var (orgId, clientId) = SeedClient(null);
        SeedBrandManual(orgId, clientId, new List<ColorEntry>
        {
            new("Unclassified", "#888888", null, 0),
            new("Primary", "#FF0000", ColorRole.Primary, 0),
        });
        await _db.SaveChangesAsync();

        var snapshot = await _loader.LoadAsync(orgId, clientId);

        var palette = snapshot.Brand!.ColorPalette;
        Assert.Equal(2, palette.Count);
        Assert.Equal(ColorRole.Primary, palette[0].Role);
        Assert.Null(palette[1].Role);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private (Guid orgId, Guid clientId) SeedClient(BackgroundSourcingPolicy? policy)
    {
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        _db.SocialClients.Add(new SocialClientData
        {
            Id = clientId,
            OrganizationId = orgId,
            Name = "Test Client",
            IsActive = true,
            BackgroundSourcingPolicy = policy.HasValue ? (short)policy.Value : null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        return (orgId, clientId);
    }

    private void SeedBrandManual(Guid orgId, Guid clientId, List<ColorEntry> palette)
    {
        _db.ClientBrandManuals.Add(new ClientBrandManualData
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OrganizationId = orgId,
            Title = "Brand",
            ColorPalette = palette,
            Logos = new(),
            BackgroundTemplates = new(),
            Typography = new(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });
    }
}
