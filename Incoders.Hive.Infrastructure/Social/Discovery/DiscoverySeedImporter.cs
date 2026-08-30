using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Infrastructure.Social.Providers.Apify;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.Infrastructure.Social.Discovery;

/// <summary>
/// Replays CAPTURED Apify niche-search output into the candidate bank at startup, so a local
/// environment has real candidates to work with instead of an empty bank and a paid run as the only
/// way to fill it.
///
/// THE MAPPING IS THE PRODUCTION ONE. Every item goes through
/// <see cref="TikTokNicheSearchAdapter"/> — the exact class a live run uses — rather than a second
/// parser written for the seed files. That is deliberate: the seed files hold VERBATIM actor items
/// (the flattened dot-notation shape a real run returned), so the import doubles as a live exercise
/// of the real mapping. If the adapter regresses, the import stops producing candidates and the
/// regression surfaces here instead of on the next billed run.
///
/// Idempotent by construction: it writes through
/// <see cref="IContentCandidateRepository.RecordNicheBankAsync"/>, whose dedup is the same
/// <see cref="CandidateIdentity"/> the discovery chain uses, so restarting the API re-imports
/// nothing. The log reports new versus already-present per file.
///
/// Failure is per FILE. A malformed envelope is logged and skipped; the remaining files still
/// import, and startup is never taken down by seed data.
///
/// Mirrors the shape and registration of <c>TikTokNicheSearchWorkflowStepSeeder</c>.
/// </summary>
public sealed class DiscoverySeedImporter
{
    private const string TikTokNetwork = "tiktok";
    private const string SourceKeyValue = "niche";

    private readonly DiscoverySeedImportOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ITenantRepository _tenants;
    private readonly IContentCandidateRepository _bank;
    private readonly IClock _clock;
    private readonly ILogger<DiscoverySeedImporter> _logger;
    private readonly TikTokNicheSearchAdapter _adapter = new();

    public DiscoverySeedImporter(
        IOptions<DiscoverySeedImportOptions> options,
        IHostEnvironment environment,
        ITenantRepository tenants,
        IContentCandidateRepository bank,
        IClock clock,
        ILogger<DiscoverySeedImporter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(tenants);
        ArgumentNullException.ThrowIfNull(bank);
        ArgumentNullException.ThrowIfNull(clock);

        _options = options.Value;
        _environment = environment;
        _tenants = tenants;
        _bank = bank;
        _clock = clock;
        _logger = logger ?? NullLogger<DiscoverySeedImporter>.Instance;
    }

    public async Task ImportAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        if (!_environment.IsDevelopment() && !_options.AllowOutsideDevelopment)
        {
            _logger.LogInformation(
                "Discovery seed import is enabled but the host environment is {EnvironmentName}. The seed is "
                    + "LOCAL TEST DATA, so nothing was imported. Set Social:DiscoverySeedImport:AllowOutsideDevelopment "
                    + "to true only if this host really should carry it.",
                _environment.EnvironmentName);
            return;
        }

        var directory = ResolveDirectory();
        if (directory is null)
        {
            _logger.LogWarning(
                "Discovery seed import is enabled but no seed directory was found at '{DirectoryPath}'. "
                    + "Nothing was imported.",
                _options.DirectoryPath);
            return;
        }

        var files = Directory.GetFiles(directory, "*.json").OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (files.Count == 0)
        {
            _logger.LogInformation(
                "Discovery seed import found no *.json envelope in '{Directory}'. Nothing was imported.",
                directory);
            return;
        }

        var organizationId = await ResolveOrganizationIdAsync(cancellationToken);
        if (organizationId is null)
        {
            return;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ImportFileAsync(file, organizationId.Value, cancellationToken);
        }
    }

    /// <summary>
    /// Imports ONE envelope. Every failure mode here is per-file and non-fatal: a malformed document,
    /// an unexpected envelope shape, or a bank write that fails are all logged and skipped so the
    /// remaining files still import and startup survives.
    /// </summary>
    private async Task ImportFileAsync(string path, Guid organizationId, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(path);

        JsonDocument document;
        try
        {
            await using var stream = File.OpenRead(path);
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(
                ex,
                "Discovery seed file '{FileName}' is malformed or unreadable and was SKIPPED. The other seed "
                    + "files are unaffected.",
                fileName);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _logger.LogError(
                    "Discovery seed file '{FileName}' is not an envelope object and was SKIPPED.",
                    fileName);
                return;
            }

            var networkKey = root.TryGetProperty("networkKey", out var networkElement)
                && networkElement.ValueKind == JsonValueKind.String
                    ? networkElement.GetString()
                    : null;

            if (!string.Equals(networkKey, TikTokNetwork, StringComparison.OrdinalIgnoreCase))
            {
                // Only TikTok has a niche-search mapping today, and mapping a non-TikTok envelope
                // through the TikTok adapter would bank rows under the wrong network key.
                _logger.LogWarning(
                    "Discovery seed file '{FileName}' declares networkKey '{NetworkKey}', which has no niche-search "
                        + "adapter. It was SKIPPED.",
                    fileName,
                    networkKey ?? "(absent)");
                return;
            }

            var hashtags = ReadHashtags(root);
            if (hashtags.Count == 0)
            {
                _logger.LogWarning(
                    "Discovery seed file '{FileName}' carries no hashtags. The bank key is "
                        + "(organization, network, hashtags), so there is nothing to file it under — SKIPPED.",
                    fileName);
                return;
            }

            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning(
                    "Discovery seed file '{FileName}' has no 'items' array and was SKIPPED.",
                    fileName);
                return;
            }

            // The production mapping, unchanged — see the class remarks for why this is not a
            // seed-specific parser.
            var (mapped, totalCount, skippedCount, providerErrors) = _adapter.MapItems(items);

            if (mapped.Count == 0)
            {
                _logger.LogWarning(
                    "Discovery seed file '{FileName}': the production adapter mapped 0 of {TotalCount} captured "
                        + "item(s). Either the file is not real actor output or the adapter's mapping regressed.",
                    fileName,
                    totalCount);
                return;
            }

            var observedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc));

            var observations = mapped
                .Select(candidate => new CandidateObservation(
                    // The SAME identity the chain and the bank dedup with — this is what makes a
                    // restart re-import nothing.
                    CandidateKey: CandidateIdentity.From(candidate.NetworkSource, candidate.ContentUrl),
                    NetworkKey: TikTokNetwork,
                    SourceKey: SourceKeyValue,
                    SearchCriterion: SourceKeyValue,
                    ContentUrl: candidate.ContentUrl,
                    AuthorHandle: candidate.SourceHandle,
                    Title: candidate.Title,
                    Summary: candidate.Summary,
                    ThumbnailUrl: candidate.ThumbnailUrl,
                    PublishedAtUtc: candidate.PublishedAtUtc,
                    Views: candidate.Views,
                    Likes: candidate.Likes,
                    Comments: candidate.Comments,
                    Shares: candidate.Shares,
                    Saves: candidate.Saves,
                    // Scoring and classification happen further down the chain; the bank stores the
                    // observation as it arrived rather than inventing a score no run computed.
                    OpportunityScore: null,
                    Classification: null,
                    ObservedAtUtc: observedAtUtc))
                .ToList();

            try
            {
                var banked = await _bank.RecordNicheBankAsync(
                    organizationId,
                    TikTokNetwork,
                    hashtags,
                    observations,
                    cancellationToken);

                _logger.LogInformation(
                    "Discovery seed file '{FileName}': {NewCount} new, {ExistingCount} already present of "
                        + "{MappedCount} mapped candidate(s) ({TotalCount} captured, {SkippedCount} unmappable, "
                        + "{ProviderErrorCount} provider error item(s)) banked for organization {OrganizationId} "
                        + "on {NetworkKey} under [{Hashtags}].",
                    fileName,
                    banked,
                    observations.Count - banked,
                    observations.Count,
                    totalCount,
                    skippedCount,
                    providerErrors.Count,
                    organizationId,
                    TikTokNetwork,
                    string.Join(", ", hashtags));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Discovery seed file '{FileName}' could not be banked for organization {OrganizationId}. "
                        + "The remaining seed files are unaffected.",
                    fileName,
                    organizationId);
            }
        }
    }

    /// <summary>
    /// The configured organization, or the single active one when exactly one exists. Zero or several
    /// active organizations with nothing configured is a REFUSAL, not a guess: banking somebody
    /// else's captured niche into a real tenant is worse than importing nothing.
    /// </summary>
    private async Task<Guid?> ResolveOrganizationIdAsync(CancellationToken cancellationToken)
    {
        if (_options.OrganizationId is { } configured && configured != Guid.Empty)
        {
            return configured;
        }

        var (tenants, _) = await _tenants.ListAsync(
            new TenantQuery(NameContains: null, IncludeInactive: false, Page: 1, PageSize: int.MaxValue),
            cancellationToken);

        var active = tenants.Where(tenant => tenant.IsActive).ToList();
        if (active.Count == 1)
        {
            return active[0].Id.Value;
        }

        _logger.LogWarning(
            "Discovery seed import found {ActiveOrganizationCount} active organization(s) and no configured "
                + "target, so it cannot tell which bank the seed belongs to. NOTHING was imported — set "
                + "Social:DiscoverySeedImport:OrganizationId to the organization that should receive it.",
            active.Count);

        return null;
    }

    /// <summary>
    /// Resolves <see cref="DiscoverySeedImportOptions.DirectoryPath"/>. An absolute path is used as
    /// given; a relative one is probed against the host content root and then the application base
    /// directory, which is what makes the same setting work from the project directory and from a
    /// published output folder. Returns <c>null</c> when no candidate exists.
    /// </summary>
    private string? ResolveDirectory()
    {
        var configured = _options.DirectoryPath;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        if (Path.IsPathRooted(configured))
        {
            return Directory.Exists(configured) ? configured : null;
        }

        foreach (var root in new[] { _environment.ContentRootPath, AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var candidate = Path.Combine(root, configured);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ReadHashtags(JsonElement root)
    {
        if (!root.TryGetProperty("hashtags", out var hashtags) || hashtags.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return hashtags
            .EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.String)
            .Select(element => element.GetString()!)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
