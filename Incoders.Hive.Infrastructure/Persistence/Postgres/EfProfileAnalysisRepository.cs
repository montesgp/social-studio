using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

/// <summary>
/// EF implementation of <see cref="IProfileAnalysisRepository"/>.
/// All queries are scoped to <paramref name="organizationId"/> — the org-scoping invariant.
/// Maps between <see cref="ProfileAnalysisData"/> (EF entity) and <see cref="ProfileAnalysis"/>
/// (Domain aggregate). No EF or Npgsql types are exposed outside this class.
/// Mirrors <see cref="EfSingleContentAnalysisRepository"/> conventions.
/// </summary>
public sealed class EfProfileAnalysisRepository : IProfileAnalysisRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfProfileAnalysisRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(
        ProfileAnalysis analysis,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        _dbContext.ProfileAnalyses.Add(ToData(analysis));
        return Task.CompletedTask;
    }

    public async Task<ProfileAnalysis?> GetByIdAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.ProfileAnalyses
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == analysisId && x.OrganizationId == organizationId,
                cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    public async Task<ProfileAnalysisHistoryPage> GetByOrganizationAsync(
        Guid organizationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize switch
        {
            < 1 => 20,
            > 100 => 100,
            _ => pageSize,
        };

        var baseQuery = _dbContext.ProfileAnalyses
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var rows = await baseQuery
            .GroupJoin(
                _dbContext.SocialClients.AsNoTracking(),
                analysis => analysis.ClientId,
                client => (Guid?)client.Id,
                (analysis, clients) => new { analysis, clients })
            .SelectMany(
                item => item.clients.DefaultIfEmpty(),
                (item, client) => new { item.analysis, client })
            .OrderByDescending(x => x.analysis.CreatedAtUtc)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(x => new ProfileAnalysisHistoryItem(
                x.analysis.Id,
                x.analysis.OrganizationId,
                x.analysis.ClientId,
                x.analysis.OwnAccountId,
                x.client == null ? null : x.client.Name,
                x.analysis.ProfileUrlOrHandle,
                x.analysis.Status,
                x.analysis.FailureCode,
                x.analysis.CreatedAtUtc,
                x.analysis.UpdatedAtUtc,
                x.analysis.CompletedAtUtc))
            .ToListAsync(cancellationToken);

        return new ProfileAnalysisHistoryPage(rows, totalCount, safePage, safePageSize);
    }

    public async Task UpdateAsync(
        ProfileAnalysis analysis,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.ProfileAnalyses
            .FirstOrDefaultAsync(
                x => x.Id == analysis.Id && x.OrganizationId == analysis.OrganizationId,
                cancellationToken);

        if (row is null)
        {
            return;
        }

        row.ClientId = analysis.ClientId;
        row.OwnAccountId = analysis.OwnAccountId;
        row.ProfileUrlOrHandle = analysis.ProfileUrlOrHandle;
        row.Status = analysis.Status.ToString();
        row.RawPostsDatasetJson = analysis.RawPostsDatasetJson;
        row.ServerDerivedStatsJson = analysis.ServerDerivedStatsJson;
        row.ProfileSnapshotJson = analysis.ProfileSnapshotJson;
        row.AiAnalysisJson = analysis.AiAnalysisJson;
        row.ProviderDiagnosticsJson = analysis.ProviderDiagnosticsJson;
        row.FailureCode = analysis.FailureCode;
        row.UpdatedAtUtc = analysis.UpdatedAtUtc;
        row.CompletedAtUtc = analysis.CompletedAtUtc;
    }

    public async Task<bool> DeleteAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.ProfileAnalyses
            .FirstOrDefaultAsync(
                x => x.Id == analysisId && x.OrganizationId == organizationId,
                cancellationToken);

        if (row is null)
        {
            return false;
        }

        _dbContext.ProfileAnalyses.Remove(row);
        return true;
    }

    /// <inheritdoc />
    public async Task<DateTime?> GetLatestAnalyzedCompletedAtAsync(
        Guid orgId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProfileAnalyses
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == orgId &&
                x.ClientId == clientId &&
                x.Status == "Analyzed" &&
                x.CompletedAtUtc != null)
            .MaxAsync(x => (DateTime?)x.CompletedAtUtc, cancellationToken);
    }

    /// <summary>
    /// A real analysis (scrape + AI stages) finishes in minutes; one untouched for this long was
    /// abandoned and must not keep debouncing refreshes.
    /// </summary>
    internal static readonly TimeSpan InFlightAnalysisStaleAfter = TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public async Task<bool> HasNonTerminalAnalysisForClientAsync(
        Guid orgId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        // Non-terminal statuses: Requested, Scraping, ProfileScraped, Analyzing.
        // Terminal statuses: Analyzed, Failed.
        //
        // Only a RECENTLY-touched non-terminal analysis is in flight. Nothing sweeps profile analyses
        // orphaned by a restart or a suspended host, and the in-memory job queue does not survive
        // either, so an abandoned "Analyzing" row would otherwise suppress every future refresh for
        // the client forever.
        var inFlightSince = DateTime.UtcNow - InFlightAnalysisStaleAfter;
        return await _dbContext.ProfileAnalyses
            .AsNoTracking()
            .AnyAsync(x =>
                x.OrganizationId == orgId &&
                x.ClientId == clientId &&
                x.Status != "Analyzed" &&
                x.Status != "Failed" &&
                x.UpdatedAtUtc >= inFlightSince,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> LatestAnalyzedHasAestheticAsync(
        Guid orgId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        // Look at the single most-recent Analyzed analysis and report whether it carries a
        // MATERIALLY USABLE aesthetic payload — not just a non-empty JSON blob (issue #84). An
        // old/near-empty analysis (e.g. only PromptVersion + ReferenceImages populated, no
        // ColorPairings/BackgroundPattern) used to satisfy the old `!IsNullOrWhiteSpace(row)`
        // check trivially, routing ClientAestheticEnsureFreshService.LatestUsableAestheticPresentAsync
        // (and ContentGenerationJobProcessor's pre-loop branch) onto the fast fire-and-forget
        // EnsureFreshAsync path instead of the blocking EnsureAvailableBlockingAsync wait — even
        // while a richer reanalysis was already running in the background. "Usable" now requires
        // real color/background signal (ColorPairings and/or BackgroundPattern), mirroring the
        // richness pillars AestheticFreshness.Evaluate already treats as material.
        var row = await _dbContext.ProfileAnalyses
            .AsNoTracking()
            .Where(x => x.OrganizationId == orgId && x.ClientId == clientId && x.Status == "Analyzed")
            .OrderByDescending(x => x.CompletedAtUtc)
            .Select(x => x.AestheticProfileJson)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(row))
        {
            return false;
        }

        var read = AestheticProfilePersistence.Read(row);
        return read.ColorPairings is { Count: > 0 } || read.BackgroundPattern is { Count: > 0 };
    }

    /// <inheritdoc />
    public async Task<AestheticFreshnessSnapshot> GetLatestAestheticFreshnessAsync(
        Guid orgId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        var json = await _dbContext.ProfileAnalyses
            .AsNoTracking()
            .Where(x => x.OrganizationId == orgId && x.ClientId == clientId && x.Status == "Analyzed")
            .OrderByDescending(x => x.CompletedAtUtc)
            .Select(x => x.AestheticProfileJson)
            .FirstOrDefaultAsync(cancellationToken);

        var read = AestheticProfilePersistence.Read(json);

        return new AestheticFreshnessSnapshot(
            SchemaVersion: read.SchemaVersion,
            HasColorPairings: read.ColorPairings is { Count: > 0 },
            HasBackgroundPattern: read.BackgroundPattern is { Count: > 0 },
            HasReferenceImages: read.ReferenceImages is { Count: > 0 });
    }

    /// <inheritdoc />
    public async Task SetAestheticProfileJsonAsync(
        Guid analysisId,
        Guid organizationId,
        string aestheticProfileJson,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.ProfileAnalyses
            .FirstOrDefaultAsync(
                x => x.Id == analysisId && x.OrganizationId == organizationId,
                cancellationToken);

        if (row is null)
        {
            return;
        }

        row.AestheticProfileJson = aestheticProfileJson;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LatestProfileAnalysisSummary?> GetLatestForClientAsync(
        Guid orgId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProfileAnalyses
            .AsNoTracking()
            .Where(x => x.OrganizationId == orgId && x.ClientId == clientId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new LatestProfileAnalysisSummary(
                x.Id,
                x.Status,
                x.CompletedAtUtc,
                x.ProfileUrlOrHandle,
                x.AestheticProfileJson))
            .FirstOrDefaultAsync(cancellationToken);
    }

    // ── Mapping helpers ────────────────────────────────────────────────────────

    private static ProfileAnalysisData ToData(ProfileAnalysis analysis) => new()
    {
        Id = analysis.Id,
        OrganizationId = analysis.OrganizationId,
        ClientId = analysis.ClientId,
        OwnAccountId = analysis.OwnAccountId,
        ProfileUrlOrHandle = analysis.ProfileUrlOrHandle,
        Status = analysis.Status.ToString(),
        RawPostsDatasetJson = analysis.RawPostsDatasetJson,
        ServerDerivedStatsJson = analysis.ServerDerivedStatsJson,
        ProfileSnapshotJson = analysis.ProfileSnapshotJson,
        AiAnalysisJson = analysis.AiAnalysisJson,
        ProviderDiagnosticsJson = analysis.ProviderDiagnosticsJson,
        FailureCode = analysis.FailureCode,
        CreatedAtUtc = analysis.CreatedAtUtc,
        UpdatedAtUtc = analysis.UpdatedAtUtc,
        CompletedAtUtc = analysis.CompletedAtUtc,
    };

    private static ProfileAnalysis ToDomain(ProfileAnalysisData row)
    {
        // Reconstruct the aggregate via the internal backdoor constructor used by EF/mapping.
        // Domain keeps a parameterless private constructor; we use reflection-free mapping through
        // a dedicated static factory that accepts raw column values — same pattern as the domain aggregate.
        return ProfileAnalysis.Reconstitute(
            id: row.Id,
            organizationId: row.OrganizationId,
            clientId: row.ClientId,
            ownAccountId: row.OwnAccountId,
            profileUrlOrHandle: row.ProfileUrlOrHandle,
            status: Enum.Parse<ProfileAnalysisStatus>(row.Status),
            rawPostsDatasetJson: row.RawPostsDatasetJson,
            serverDerivedStatsJson: row.ServerDerivedStatsJson,
            profileSnapshotJson: row.ProfileSnapshotJson,
            aiAnalysisJson: row.AiAnalysisJson,
            providerDiagnosticsJson: row.ProviderDiagnosticsJson,
            failureCode: row.FailureCode,
            createdAtUtc: row.CreatedAtUtc,
            updatedAtUtc: row.UpdatedAtUtc,
            completedAtUtc: row.CompletedAtUtc);
    }
}
