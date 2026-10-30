using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Social.Networks;
using Microsoft.Extensions.Logging;

namespace SocialStudio.Infrastructure.Social;

/// <summary>
/// Seeds the platform.social_networks catalog with deterministic initial entries.
/// Resolution 1: upsert-on-missing-only — if a row already exists by code, SKIP it.
/// PlatformAdmin changes (renames, deactivations) are preserved across restarts.
/// </summary>
public sealed class SocialNetworkCatalogSeeder
{
    // Deterministic GUIDs — pinned per ADR 7. DO NOT change these values.
    // Downstream SDDs will FK against these IDs. Changing them is a breaking change.
    // Resolution 1: this comment prevents drift back to an update branch.
    private static readonly SeedDefinition[] Catalog =
    [
        new(new Guid("00000000-0000-0000-0001-000000000001"), "instagram", "Instagram", "instagram"),
        new(new Guid("00000000-0000-0000-0001-000000000002"), "facebook",  "Facebook",  "facebook"),
        new(new Guid("00000000-0000-0000-0001-000000000003"), "tiktok",    "TikTok",    "tiktok"),
    ];

    private readonly ISocialNetworkRepository _networks;
    private readonly IClock _clock;
    private readonly ILogger<SocialNetworkCatalogSeeder> _logger;

    public SocialNetworkCatalogSeeder(
        ISocialNetworkRepository networks,
        IClock clock,
        ILogger<SocialNetworkCatalogSeeder> logger)
    {
        _networks = networks;
        _clock = clock;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var seed in Catalog)
        {
            // Resolution 1: upsert-on-missing-only. If existing != null → SKIP.
            // No update branch. PlatformAdmin changes survive restarts.
            var existing = await _networks.GetByCodeAsync(seed.Code, cancellationToken);
            if (existing is not null)
            {
                continue;
            }

            var createResult = SocialNetwork.Create(seed.Code, seed.DisplayName, seed.IconKey, _clock.UtcNow);
            if (createResult.IsFailure)
            {
                _logger.LogError(
                    "Failed to seed social network '{Code}': {Error}",
                    seed.Code,
                    createResult.Error.Code);
                throw new InvalidOperationException(
                    $"Failed to seed social network '{seed.Code}': {createResult.Error.Code}");
            }

            // Use Restore to preserve the deterministic GUID (Create generates a new one)
            var network = SocialNetwork.Restore(
                new SocialNetworkId(seed.Id),
                createResult.Value.Code,
                createResult.Value.DisplayName,
                createResult.Value.IconKey,
                isActive: true,
                createdAtUtc: _clock.UtcNow,
                updatedAtUtc: _clock.UtcNow);

            await _networks.AddAsync(network, cancellationToken);
            await _networks.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Seeded social network '{Code}' ({Id})", seed.Code, seed.Id);
        }
    }

    private sealed record SeedDefinition(Guid Id, string Code, string DisplayName, string IconKey);
}
