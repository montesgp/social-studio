using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// EF-backed read model for retry/cost-control decisions in content generation.
/// Counts failed per-format attempts across jobs for one analysis/client scope.
/// </summary>
public sealed class EfContentGenerationFailurePolicy : IContentGenerationFailurePolicy
{
    private static readonly string[] FailedFormatStatuses = ["AiFailed", "RenderFailed", "Failed"];

    private readonly AppIdentityDbContext _db;

    public EfContentGenerationFailurePolicy(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyDictionary<string, int>> CountFailedAttemptsByFormatAsync(
        Guid organizationId,
        Guid analysisId,
        Guid clientId,
        IReadOnlyCollection<string> formatKeys,
        CancellationToken cancellationToken)
    {
        var normalizedFormatKeys = formatKeys
            .Select(format => format.Trim().ToLowerInvariant())
            .Where(format => format.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedFormatKeys.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var rows = await (
                from job in _db.ContentGenerationJobs.AsNoTracking()
                join result in _db.ContentGenerationFormatResults.AsNoTracking()
                    on job.Id equals result.JobId
                where job.OrganizationId == organizationId
                      && job.AnalysisId == analysisId
                      && job.ClientId == clientId
                      && normalizedFormatKeys.Contains(result.FormatKey.ToLower())
                      && FailedFormatStatuses.Contains(result.Status)
                select result.FormatKey)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(format => format, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
    }
}
