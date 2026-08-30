using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.Niches;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Infrastructure.Social;

/// <summary>
/// Seeds the platform.niches catalog with a small deterministic starter set.
/// Upsert-on-missing-only, mirroring <see cref="SocialNetworkCatalogSeeder"/>: if a row already
/// exists by code it is SKIPPED, so sysadmin edits (renames, hashtag curation, deactivations)
/// survive restarts.
/// </summary>
public sealed class NicheCatalogSeeder
{
    // Deterministic GUIDs. DO NOT change these values — client associations FK against them.
    private static readonly SeedDefinition[] Catalog =
    [
        new(
            new Guid("00000000-0000-0000-0002-000000000001"),
            "software-technology",
            "Software & Technology",
            "cpu",
            ["software", "technology"]),
        new(
            new Guid("00000000-0000-0000-0002-000000000002"),
            "medical-health",
            "Medical & Health",
            "stethoscope",
            ["medical", "health"]),
        new(
            new Guid("00000000-0000-0000-0002-000000000003"),
            "gastronomy",
            "Gastronomy",
            "utensils",
            ["gastronomy", "food"]),
        new(
            new Guid("00000000-0000-0000-0002-000000000004"),
            "fitness-wellness",
            "Fitness & Wellness",
            "dumbbell",
            ["fitness", "wellness"]),
        new(
            new Guid("00000000-0000-0000-0002-000000000005"),
            "real-estate",
            "Real Estate",
            "home",
            ["realestate", "property"]),
    ];

    private readonly INicheRepository _niches;
    private readonly IClock _clock;
    private readonly ILogger<NicheCatalogSeeder> _logger;

    public NicheCatalogSeeder(
        INicheRepository niches,
        IClock clock,
        ILogger<NicheCatalogSeeder> logger)
    {
        _niches = niches;
        _clock = clock;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var seed in Catalog)
        {
            var existing = await _niches.GetByCodeAsync(seed.Code, cancellationToken);
            if (existing is not null)
            {
                continue;
            }

            // Create validates and normalizes; Restore then pins the deterministic id.
            var createResult = Niche.Create(
                seed.Code,
                seed.DisplayName,
                seed.IconKey,
                seed.SearchHashtags,
                _clock.UtcNow);

            if (createResult.IsFailure)
            {
                _logger.LogError(
                    "Failed to seed niche '{Code}': {Error}",
                    seed.Code,
                    createResult.Error.Code);
                throw new InvalidOperationException(
                    $"Failed to seed niche '{seed.Code}': {createResult.Error.Code}");
            }

            var niche = Niche.Restore(
                new NicheId(seed.Id),
                createResult.Value.Code,
                createResult.Value.DisplayName,
                createResult.Value.IconKey,
                createResult.Value.SearchHashtags,
                isActive: true,
                createdAtUtc: _clock.UtcNow,
                updatedAtUtc: _clock.UtcNow);

            await _niches.AddAsync(niche, cancellationToken);
            await _niches.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Seeded niche '{Code}' ({Id})", seed.Code, seed.Id);
        }
    }

    private sealed record SeedDefinition(
        Guid Id,
        string Code,
        string DisplayName,
        string IconKey,
        string[] SearchHashtags);
}
