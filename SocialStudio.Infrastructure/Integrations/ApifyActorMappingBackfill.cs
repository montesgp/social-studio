using System.Text.Json;
using System.Text.Json.Serialization;
using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Infrastructure.Integrations;

/// <summary>
/// Idempotent startup backfill that re-tags Apify actor_mappings_json entries whose
/// variantKey is null, empty, or "default".
///
/// Transformation rule (per user spec):
///   For entries whose variantKey is null/empty/"default":
///     - The LAST TWO entries in the array → variantKey "profile"
///     - All earlier entries → variantKey "social-extraction"
///   Within each resulting (networkKey, variantKey) group, set isPrimary=true on the
///   FIRST entry (array order after re-tagging) and false on the rest.
///   Entries already having an explicit non-default variantKey are left unchanged.
///   Only rewrites an org's credential if it actually had "default"/missing entries
///   (idempotent — running twice is a no-op).
///
/// The pure transformation logic is extracted as <see cref="Transform"/> and
/// <see cref="HasAnyDefaultEntries"/> to be testable independently of encryption/DB.
/// </summary>
public sealed class ApifyActorMappingBackfill
{
    private static readonly Guid ApifyProviderId = new("83b6f049-f598-47e9-a1c5-77d1f57cb694");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly IOrganizationIntegrationCredentialRepository _credentials;
    private readonly IIntegrationSecretProtector _protector;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ApifyActorMappingBackfill> _logger;

    public ApifyActorMappingBackfill(
        IOrganizationIntegrationRepository integrations,
        IOrganizationIntegrationCredentialRepository credentials,
        IIntegrationSecretProtector protector,
        IUnitOfWork unitOfWork,
        ILogger<ApifyActorMappingBackfill>? logger = null)
    {
        _integrations = integrations;
        _credentials = credentials;
        _protector = protector;
        _unitOfWork = unitOfWork;
        _logger = logger ?? NullLogger<ApifyActorMappingBackfill>.Instance;
    }

    /// <summary>
    /// Scans all orgs' Apify integrations and re-tags any "default" actor_mappings_json entries.
    /// Safe to run multiple times (idempotent — orgs without "default" entries are skipped).
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        // Query all orgs that have an Apify integration (DataExtraction category).
        // We get all integrations across all orgs by listing by provider id.
        // Since IOrganizationIntegrationRepository only has org-scoped queries, we
        // approach this differently: we scan all orgs via the credential repository
        // by finding credentials with key="actor_mappings_json" for the Apify provider.

        // Strategy: use IOrganizationIntegrationCredentialRepository — it lists by integration id.
        // We can't enumerate all orgs from the repo interface, so we instead find all Apify
        // integration credentials by scanning via the integration id method.
        // Given repo limitations, we use ListByOrganizationAsync on representative orgs — but
        // since we don't have an "all orgs" query, we need to enumerate org integrations.
        // The cleanest approach is to add a method or iterate over known orgs. In this design
        // we iterate over ALL organization integrations whose provider is Apify via a
        // dedicated EF query. We extend IOrganizationIntegrationRepository with a new method
        // ListByProviderIdAsync in the seeder-only scope using a raw EF dbContext.
        // However, to avoid touching the repository interface contract (ports), we instead
        // implement the scan directly using available interfaces.

        // Practical approach: since we can only query integrations per org and there's no
        // "list all orgs" interface, we query all Apify integrations across all orgs via
        // IOrganizationIntegrationCredentialRepository by checking all integration credentials
        // whose integration belongs to the Apify provider. We do this through the
        // IOrganizationIntegrationRepository.ListByOrganizationAsync method, but without a
        // list-all-orgs primitive we instead inject a "scan all" capability.
        // The real implementation approach: we accept that the backfill seeder runs at startup
        // and needs a broader scan. We inject the AppDbContext directly (infrastructure-only,
        // only used here, no domain boundary violation since this is a startup task in infra).

        // NOTE: Since no "list all orgs" primitive exists in the repo interfaces, and this is
        // a startup task, we use the IOrganizationIntegrationRepository's available methods
        // to find orgs. We do this via a specialized overload that accepts the apify provider id.
        // For now, the scan logic is delegated to a method that uses direct EF — but to keep
        // the interface clean we define an IApifyIntegrationScanner internal port here.

        // Simpler: the backfill only needs to run once. The IOrganizationIntegrationCredentialRepository
        // has no "find all by provider" method. We'll use IOrganizationIntegrationRepository
        // through a new internal-only method via a cast (seam for testability).
        // For the purposes of this startup task we inject IApifyIntegrationScanner.

        await BackfillAllOrgsAsync(ct);
    }

    private async Task BackfillAllOrgsAsync(CancellationToken ct)
    {
        // The IOrganizationIntegrationRepository doesn't expose a list-all-providers query.
        // We introduce IApifyIntegrationScanner as a thin infrastructure-only port that
        // the EF implementation can satisfy. The scanner is registered in DI alongside this class.
        // Since we want minimal surface, cast to the scanner if available — otherwise no-op.
        if (_integrations is not IApifyIntegrationScanner scanner)
        {
            _logger.LogWarning(
                "ApifyActorMappingBackfill: IOrganizationIntegrationRepository does not implement " +
                "IApifyIntegrationScanner — backfill skipped. Register EfApifyIntegrationScanner.");
            return;
        }

        var apifyIntegrationIds = await scanner.ListApifyIntegrationIdsAsync(
            new IntegrationProviderId(ApifyProviderId), ct);

        _logger.LogInformation(
            "ApifyActorMappingBackfill: scanning {Count} Apify integrations for legacy 'default' actor entries.",
            apifyIntegrationIds.Count);

        var updatedCount = 0;

        foreach (var integrationId in apifyIntegrationIds)
        {
            try
            {
                updatedCount += await BackfillSingleIntegrationAsync(integrationId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ApifyActorMappingBackfill: error processing integration {IntegrationId} — skipping.",
                    integrationId.Value);
            }
        }

        _logger.LogInformation(
            "ApifyActorMappingBackfill: complete. Updated {Updated} of {Total} integrations.",
            updatedCount,
            apifyIntegrationIds.Count);
    }

    private async Task<int> BackfillSingleIntegrationAsync(
        OrganizationIntegrationId integrationId,
        CancellationToken ct)
    {
        var allCredentials = await _credentials.ListByIntegrationIdAsync(integrationId, ct);

        var mappingCredential = allCredentials
            .FirstOrDefault(c => string.Equals(c.Key, "actor_mappings_json", StringComparison.OrdinalIgnoreCase));

        if (mappingCredential is null)
        {
            return 0;
        }

        string json;
        try
        {
            json = _protector.Unprotect(mappingCredential.EncryptedValue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "ApifyActorMappingBackfill: could not decrypt actor_mappings_json for integration {IntegrationId} — skipping.",
                integrationId.Value);
            return 0;
        }

        List<RawEntry> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<RawEntry>>(json, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "ApifyActorMappingBackfill: could not parse actor_mappings_json for integration {IntegrationId} — skipping.",
                integrationId.Value);
            return 0;
        }

        if (!HasAnyDefaultEntries(entries))
        {
            // Already tagged — idempotent skip
            return 0;
        }

        var transformed = Transform(entries);
        var newJson = JsonSerializer.Serialize(transformed, JsonOptions);
        var encrypted = _protector.Protect(newJson);

        var rotateResult = mappingCredential.Rotate(encrypted, DateTime.UtcNow);
        if (rotateResult.IsFailure)
        {
            _logger.LogWarning(
                "ApifyActorMappingBackfill: Rotate failed for integration {IntegrationId}: {Error}",
                integrationId.Value,
                rotateResult.Error.Code);
            return 0;
        }

        await _credentials.UpsertAsync(mappingCredential, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "ApifyActorMappingBackfill: updated actor_mappings_json for integration {IntegrationId}. " +
            "Entries before={Before} after={After}.",
            integrationId.Value,
            entries.Count,
            transformed.Count);

        return 1;
    }

    // ── Pure transformation logic — no I/O, fully testable ────────────────────

    /// <summary>
    /// Returns true if any entry has a null, empty, or "default" variantKey (needs backfill).
    /// </summary>
    public static bool HasAnyDefaultEntries(IReadOnlyList<RawEntry> entries) =>
        entries.Any(e => IsDefaultVariant(e.VariantKey));

    /// <summary>
    /// Pure function: transforms a list of actor mapping entries so that all "default"/null/empty
    /// variantKey entries are re-tagged to "profile" (last 2) or "social-extraction" (earlier),
    /// with isPrimary set on the first entry within each (networkKey, variantKey) group.
    /// Already-tagged entries (non-default, non-empty variantKey) are passed through unchanged.
    /// </summary>
    public static List<RawEntry> Transform(List<RawEntry> entries)
    {
        if (entries.Count == 0) return [];

        // Collect entries that need re-tagging (null/empty/"default" variantKey)
        var toRetag = entries
            .Where(e => IsDefaultVariant(e.VariantKey))
            .ToList();

        // Apply re-tagging rule: last 2 → "profile"; all earlier → "social-extraction".
        var retagged = new List<RawEntry>(toRetag.Count);
        var defaultCount = toRetag.Count;
        for (var i = 0; i < defaultCount; i++)
        {
            var newVariant = (i >= defaultCount - 2) ? "profile" : "social-extraction";
            retagged.Add(toRetag[i] with { VariantKey = newVariant, IsPrimary = false });
        }

        // Rebuild in original array order, substituting re-tagged entries for "default" slots.
        var retagIdx = 0;
        var inOriginalOrder = entries.Select(entry =>
            IsDefaultVariant(entry.VariantKey)
                ? retagged[retagIdx++]
                : entry).ToList();

        // Set isPrimary: first entry in each (networkKey, variantKey) group → true.
        // HashSet.Add returns true on first insertion (case-insensitive composite key).
        var seenGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return inOriginalOrder
            .Select(entry =>
            {
                var groupKey = $"{entry.NetworkKey?.ToLowerInvariant()}|{entry.VariantKey?.ToLowerInvariant()}";
                return entry with { IsPrimary = seenGroups.Add(groupKey) };
            })
            .ToList();
    }

    private static bool IsDefaultVariant(string? variantKey) =>
        string.IsNullOrWhiteSpace(variantKey)
        || string.Equals(variantKey.Trim(), "default", StringComparison.OrdinalIgnoreCase);

    // ── DTO used for JSON de/serialisation — matches actor_mappings_json contract ──

    public sealed record RawEntry(
        [property: JsonPropertyName("networkKey")]  string NetworkKey,
        [property: JsonPropertyName("variantKey")]  string? VariantKey,
        [property: JsonPropertyName("actorId")]     string ActorId,
        [property: JsonPropertyName("isPrimary")]   bool IsPrimary);
}

/// <summary>
/// Infrastructure-only port: returns all OrganizationIntegrationId values for a given provider.
/// Implemented by the EF repository layer so the backfill can scan across orgs without
/// exposing a "list all orgs" API on the domain repository interface.
/// </summary>
public interface IApifyIntegrationScanner
{
    Task<IReadOnlyList<OrganizationIntegrationId>> ListApifyIntegrationIdsAsync(
        IntegrationProviderId providerId,
        CancellationToken cancellationToken = default);
}
